using System;
using System.Linq;
using Godot;
using Injectra.Godot.Diagnostics;

namespace Injectra.Godot.Tests;

internal static class DiagnosticsTests
{
	internal static void Run(Node parent)
	{
		CaptureIsReadOnlyAndIncludesSiblingContainers();
		AllScopesHaveTheirActualPaths(parent);
		PanelShowsDetailsAndClearsWhenStopped(parent);
		DiagnosticsTreeTests.Run(parent);
		EmptySnapshotIsHandled(parent);
		GD.Print("PASS: editor diagnostics payload, read-only capture, tree rendering and session cleanup.");
	}

	internal static void RunIndependentRoots(Node parent)
	{
		Check(InjectraProjectScope.Active is null, "This scenario requires independent roots, without an Autoload project scope.");
		var source = new InjectraScope { Name = "SourceRoot" };
		var placement = new InjectraScope { Name = "PlacementRoot" };
		parent.AddChild(source);
		parent.AddChild(placement);
		using var scene = new PackedScene();
		var template = new InjectraScope { Name = "CrossPlaced" };
		Check(scene.Pack(template) == Error.Ok, "Packing the cross-placed scope failed.");
		template.Free();
		var child = source.Container.Resolve<SceneFactory>().Instantiate<InjectraScope>(scene, placement);

		var rows = DiagnosticsBridge.CaptureTree(parent.GetTree().Root)[1].AsGodotArray();
		var childPath = $"Scope path: {child.GetPath()}\n";
		var childRow = rows.Select(value => value.AsGodotArray()).Single(row => row[5].AsString().Contains(childPath));
		var ownerRow = rows[childRow[0].AsInt32()].AsGodotArray();

		Check(ownerRow[5].AsString().Contains($"Scope path: {source.GetPath()}\n"),
			"A child must belong to its DI root's path table, even when placed under another independent root node.");
		Check(!ownerRow[5].AsString().Contains($"Scope path: {placement.GetPath()}\n"),
			"Capture-local container IDs must not mix independent roots.");
		source.Free();
		placement.Free();
		GD.Print("PASS: independent diagnostics roots and cross-placed scope paths.");
	}

	private static void CaptureIsReadOnlyAndIncludesSiblingContainers()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<PendingService>(_ =>
		{
			calls++;
			return new PendingService();
		}, Lifetime.Singleton);
		builder.RegisterInstance(new ExistingService());
		using var root = builder.Build();
		using var child = root.CreateChild(local => local.Register<ChildService>(Lifetime.Singleton));
		using var sibling = root.CreateChild();
		using var grandchild = child.CreateChild();

		var packet = DiagnosticsData.Capture([("/root/ProjectScope", root)]);
		var text = packet[0].AsString();
		var rows = packet[1].AsGodotArray();

		Check(calls == 0, "Inspection must not invoke a factory.");
		Check(text.Contains("Container #4"), "The snapshot must include sibling and grandchild containers.");
		Check(text.Contains("Dependencies: unknown (factory)"), "Factory boundaries must be explicit.");
		var existing = rows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == nameof(ExistingService));
		var pending = rows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == nameof(PendingService));
		Check(existing[2].AsString() == "● Created", "Existing instances must remain created.");
		Check(pending[2].AsString() == "○ Registered", "Pending registrations must remain pending.");
		Check(existing[5].AsString().Contains("Ownership: External"), "Details must report actual ownership.");
	}

	private static void AllScopesHaveTheirActualPaths(Node parent)
	{
		var level = new InjectraScope { Name = "DiagnosticLevel" };
		var inventory = new InjectraScope { Name = "Inventory" };
		var combat = new InjectraScope { Name = "Combat" };
		var preview = new InjectraScope { Name = "Preview" };
		parent.AddChild(level);
		level.AddChild(inventory);
		level.AddChild(combat);
		inventory.AddChild(preview);
		using var plainContainer = inventory.Container.CreateChild();
		var detachedScope = new InjectraScope { Name = "Detached" };
		detachedScope.Prepare(parentContainer: inventory.Container);

		// Native node order must not determine which DI container receives a scope path.
		level.MoveChild(combat, 0);
		var packet = DiagnosticsBridge.CaptureTree(parent.GetTree().Root);
		var rows = packet[1].AsGodotArray();
		foreach (var scope in new[] { level, inventory, combat, preview })
		{
			var id = scope.Container.GetDiagnostics().Id;
			var path = scope.GetPath().ToString();
			var row = rows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == $"Container #{id} ({scope.Name})");
			Check(row[5].AsString().Contains($"Scope path: {path}"), "Container details must include the actual Godot scope path.");
		}
		var plainId = plainContainer.GetDiagnostics().Id;
		var plainRow = rows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == $"Container #{plainId}");
		Check(plainRow[5].AsString().Contains("Scope path: none"), "A plain C# container must not borrow a neighboring scope's path.");

		var detachedId = detachedScope.Container.GetDiagnostics().Id;
		var detachedRow = rows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == $"Container #{detachedId}");
		Check(detachedRow[5].AsString().Contains("Scope path: none"), "A detached scope has no absolute scene-tree path and must not borrow one.");

		var oldPath = inventory.GetPath().ToString();
		inventory.Name = "RenamedInventory";
		var refreshedRows = DiagnosticsBridge.CaptureTree(parent.GetTree().Root)[1].AsGodotArray();
		var inventoryId = inventory.Container.GetDiagnostics().Id;
		Check(refreshedRows.Select(value => value.AsGodotArray()).Any(row => row[1].AsString() == $"Container #{inventoryId} (RenamedInventory)"),
			"Refreshing must update the scope node name in the container label.");
		var previewId = preview.Container.GetDiagnostics().Id;
		var previewRow = refreshedRows.Select(value => value.AsGodotArray()).Single(row => row[1].AsString() == $"Container #{previewId} (Preview)");
		Check(previewRow[5].AsString().Contains(preview.GetPath().ToString()), "Refreshing must update descendant paths after a rename.");
		Check(!previewRow[5].AsString().Contains(oldPath), "Refreshing must not keep stale scope paths.");
		detachedScope.Free();
		level.Free();
	}

	private static void PanelShowsDetailsAndClearsWhenStopped(Node parent)
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new ExistingService());
		using var container = builder.Build();
		using var scene = GD.Load<PackedScene>("res://addons/injectra/Diagnostics/DiagnosticsPanel.tscn");
		var panel = scene.Instantiate<DiagnosticsPanel>();
		parent.AddChild(panel);
		var requests = 0;
		panel.RefreshRequested = () => requests++;

		panel.SetActive(true);
		panel.RefreshButton.EmitSignal(Button.SignalName.Pressed);
		Check(requests == 1, "Refresh must send one request, without an automatic polling loop.");
		panel.ShowSnapshot(DiagnosticsData.Capture([("Test scope", container)]));

		var containerRow = panel.Entries.GetRoot().GetFirstChild();
		var serviceRow = containerRow.GetFirstChild();
		Check(serviceRow.GetText(0) == nameof(ExistingService), "The panel must show registrations from the payload.");
		serviceRow.Select(0);
		Check(panel.Details.Text.Contains("ExistingService"), "Selection must show service details.");
		Check(!panel.Details.Editable, "Details must be read-only.");
		Check(!panel.CopyButton.Disabled && panel.SnapshotText.Length > 0, "A captured snapshot must be available for copying.");
		Check(panel.Entries.GetColumnTitle(0) == panel.ColumnTitles[0], "Scene-authored column titles must be applied.");

		panel.SetActive(false);

		Check(panel.RefreshButton.Disabled && panel.CopyButton.Disabled, "Stopped sessions must disable runtime actions.");
		Check(panel.Entries.GetRoot() is null, "Stopped sessions must discard the previous tree.");
		Check(panel.Details.Text.Length == 0 && panel.SnapshotText.Length == 0, "Stopped sessions must discard previous details and text.");
		panel.Free();
	}

	private static void EmptySnapshotIsHandled(Node parent)
	{
		using var scene = GD.Load<PackedScene>("res://addons/injectra/Diagnostics/DiagnosticsPanel.tscn");
		var panel = scene.Instantiate<DiagnosticsPanel>();
		parent.AddChild(panel);
		panel.SetActive(true);

		panel.ShowSnapshot(DiagnosticsData.Capture([]));

		Check(panel.Status.Text.Contains("No active"), "A running game without scopes must have an explicit empty state.");
		Check(panel.CopyButton.Disabled, "An empty snapshot must not enable copying.");
		panel.Free();
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	public sealed class PendingService;
	public sealed class ExistingService;
	public sealed class ChildService;
}

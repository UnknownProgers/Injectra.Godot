using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Injectra.Godot.Diagnostics;

namespace Injectra.Godot.Tests;

internal static class DiagnosticsTreeTests
{
	internal static void Run(Node parent)
	{
		ContainersPrecedeAlphabeticalRegistrations();
		GenericSpecializationsAreGrouped();
		ExplicitClosedGenericsAreGrouped();
		MultipleGenericImplementationsKeepTheirDetails();
		NestedDependenciesAndShortcuts(parent);
		GD.Print("PASS: diagnostics sorting, implementation-only labels, generic groups, collapsed dependencies and modifier shortcuts.");
	}

	private static void ContainersPrecedeAlphabeticalRegistrations()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new ZCreated());
		builder.Register<AlphaService>(Lifetime.Singleton).As<IAlias>().AsSelf();
		using var container = builder.Build();
		using var child = container.CreateChild();
		using var sibling = container.CreateChild();
		var rows = DiagnosticsData.Capture([("Root", container)])[1].AsGodotArray();
		var direct = rows.Select(value => value.AsGodotArray()).Where(row => row[0].AsInt32() == 0).ToArray();
		Check(direct.Take(2).All(row => row[1].AsString().StartsWith("Container #", StringComparison.Ordinal)), "Child containers must precede registrations.");
		Check(direct[2][1].AsString() == "AlphaService", "Registration labels must contain only the implementation.");
		Check(direct[2][5].AsString().Contains(nameof(IAlias)), "Contracts must remain available in registration details.");
		Check(direct[2][2].AsString() == "○ Registered" && direct[3][1].AsString() == nameof(ZCreated), "Alphabetical order must take precedence over creation state.");
	}

	private static void GenericSpecializationsAreGrouped()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Logger<>), Lifetime.Singleton).As(typeof(ILogger<>));
		using var container = builder.Build();
		container.Resolve<ILogger<Leaf>>();
		container.Resolve<ILogger<AlphaService>>();
		var rows = DiagnosticsData.Capture([("Root", container)])[1].AsGodotArray();
		var group = rows.Select((value, index) => (Row: value.AsGodotArray(), Index: index))
			.Single(entry => entry.Row[1].AsString() == "ILogger<>");
		Check(group.Row[0].AsInt32() == 0 && group.Row[5].AsString().Contains("Dependencies are deferred"), "The open generic registration must be represented by its group, retaining details.");
		var members = rows.Select(value => value.AsGodotArray()).Where(row => row[0].AsInt32() == group.Index).ToArray();
		Check(members.Length == 2, "Both cached specializations must belong to one generic group.");
		Check(members[0][1].AsString() == "Logger<AlphaService>" && members[1][1].AsString() == "Logger<Leaf>", "Generic members must be alphabetical and show only implementations.");
		Check(members.All(row => row[2].AsString() == "● Created" && row[5].AsString().Contains("Cached generic specialization")), "Grouping must preserve specialization state and details.");
	}

	private static void ExplicitClosedGenericsAreGrouped()
	{
		var builder = new ContainerBuilder();
		builder.Register<Logger<Leaf>>(Lifetime.Singleton).As<ILogger<Leaf>>();
		builder.Register<Logger<AlphaService>>(Lifetime.Singleton).As<ILogger<AlphaService>>();
		using var container = builder.Build();
		var rows = DiagnosticsData.Capture([("Root", container)])[1].AsGodotArray();
		Check(rows.Count == 4, "Explicit closed generics need one group and two registrations, without a fake open registration.");
		Check(rows[1].AsGodotArray()[1].AsString() == "ILogger<>" && rows[1].AsGodotArray()[2].AsString() == "Generic group", "A synthetic group must not pretend to be a registration.");
		Check(rows.Skip(2).All(value => value.AsGodotArray()[0].AsInt32() == 1), "Explicit closed registrations must be children of the generic group.");
	}

	private static void MultipleGenericImplementationsKeepTheirDetails()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Logger<>), Lifetime.Singleton).As(typeof(ILogger<>));
		builder.Register(typeof(OtherLogger<>), Lifetime.Singleton).As(typeof(ILogger<>));
		using var container = builder.Build();
		container.ResolveAll<ILogger<Leaf>>();
		var rows = DiagnosticsData.Capture([("Root", container)])[1].AsGodotArray();
		var group = rows.Select((value, index) => (Row: value.AsGodotArray(), Index: index))
			.Single(entry => entry.Row[1].AsString() == "ILogger<>");
		var members = rows.Select(value => value.AsGodotArray()).Where(row => row[0].AsInt32() == group.Index).ToArray();
		Check(members.Length == 4, "A shared generic group must retain both definitions and both specializations.");
		Check(members.Count(row => row[2].AsString() == "● Created") == 2, "Each implementation must retain its own creation state.");
	}

	private static void NestedDependenciesAndShortcuts(Node parent)
	{
		var builder = new ContainerBuilder();
		builder.Register<GraphRoot>(Lifetime.Singleton);
		builder.Register<Branch>(Lifetime.Singleton).As<IBranch>();
		builder.Register<Leaf>(Lifetime.Singleton);
		using var container = builder.Build();
		using var scene = GD.Load<PackedScene>("res://addons/injectra/Diagnostics/DiagnosticsPanel.tscn");
		var panel = scene.Instantiate<DiagnosticsPanel>();
		parent.AddChild(panel);
		try
		{
			panel.ShowSnapshot(DiagnosticsData.Capture([("Root", container)]));
			var root = panel.Entries.GetRoot().GetFirstChild();
			Check(Descendants(root).All(item => item.Collapsed), "Every visible branch must initially be collapsed.");
			root.Collapsed = false;
			var registration = Children(root).Single(item => item.GetText(0) == nameof(GraphRoot));
			registration.Collapsed = false;
			var dependency = registration.GetFirstChild();
			Check(dependency.GetText(0) == "Branch" && dependency.Collapsed, "Opening a registration must show its direct dependency but keep nested dependencies hidden.");
			Check(dependency.GetFirstChild().GetText(0) == nameof(Leaf), "Nested dependencies must remain available for manual expansion.");
			Check(dependency.GetMetadata(0).AsString().Contains(nameof(IBranch)), "The requested contract must remain in dependency details.");

			Modifier(Key.Shift, true);
			panel.Entries.EmitSignal(Tree.SignalName.ItemCollapsed, root);
			Check(Descendants(root).All(item => !item.Collapsed), "Shift plus collapse arrow must expand the complete branch.");
			Modifier(Key.Shift, false);
			Modifier(Key.Ctrl, true);
			panel.Entries.EmitSignal(Tree.SignalName.ItemCollapsed, root);
			Check(Descendants(root).All(item => item.Collapsed), "Ctrl plus collapse arrow must collapse the complete branch.");
			Modifier(Key.Ctrl, false);
			root.Collapsed = false;
			Modifier(Key.Shift, true);
			panel.Entries.EmitSignal(Tree.SignalName.ItemCollapsed, registration);
			Check(Descendants(registration).All(item => !item.Collapsed), "Shift must expand the selected registration branch.");
			Check(Children(root).Where(item => item != registration).All(item => item.Collapsed), "Recursive expansion must leave sibling registrations closed.");
			panel.ShowSnapshot(DiagnosticsData.Capture([("Root", container)]));
			Check(Descendants(panel.Entries.GetRoot().GetFirstChild()).All(item => item.Collapsed), "Refresh while Shift is held must still start fully collapsed.");
		}
		finally
		{
			Modifier(Key.Shift, false);
			Modifier(Key.Ctrl, false);
			panel.Free();
		}
	}

	private static void Modifier(Key key, bool pressed)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
		Input.FlushBufferedEvents();
	}

	private static IEnumerable<TreeItem> Children(TreeItem item)
	{
		for (var child = item.GetFirstChild(); child is not null; child = child.GetNext())
			yield return child;
	}

	private static IEnumerable<TreeItem> Descendants(TreeItem item)
	{
		yield return item;
		foreach (var child in Children(item))
		foreach (var nested in Descendants(child))
			yield return nested;
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	public interface IAlias;
	public sealed class AlphaService : IAlias;
	public sealed class ZCreated;
	public interface ILogger<T>;
	public sealed class Logger<T> : ILogger<T>;
	public sealed class OtherLogger<T> : ILogger<T>;
	public sealed class Leaf;
	public interface IBranch;
	public sealed class Branch(Leaf leaf) : IBranch
	{
		public Leaf Leaf { get; } = leaf;
	}
	public sealed class GraphRoot(IBranch branch)
	{
		public IBranch Branch { get; } = branch;
	}
}

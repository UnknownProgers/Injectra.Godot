#nullable enable

using System.Collections.Generic;
using Godot;
using VariantArray = Godot.Collections.Array;

namespace Injectra.Godot.Diagnostics;

/// <summary>Captures diagnostics only when the connected editor requests a refresh.</summary>
public partial class DiagnosticsBridge : Node
{
	private bool _registered;

	public override void _EnterTree()
	{
		ProcessMode = ProcessModeEnum.Disabled;
		if (!EngineDebugger.IsActive())
			return;

		EngineDebugger.RegisterMessageCapture("injectra", Callable.From<string, VariantArray, bool>(Capture));
		_registered = true;
	}

	public override void _ExitTree()
	{
		if (_registered)
			EngineDebugger.UnregisterMessageCapture("injectra");
	}

	private bool Capture(string message, VariantArray data)
	{
		if (message != "refresh")
			return false;

		// The debugger can poll while a scope is still being prepared. Capture on the next idle turn.
		Callable.From(SendSnapshot).CallDeferred();
		return true;
	}

	private void SendSnapshot()
	{
		if (!_registered || !EngineDebugger.IsActive())
			return;

		EngineDebugger.SendMessage("injectra:snapshot", CaptureTree(GetTree().Root));
	}

	internal static VariantArray CaptureTree(Node sceneRoot)
	{
		var roots = new List<(string Name, Container Container)>();
		var paths = new Dictionary<Container, Dictionary<int, string>>();
		CollectScopes(sceneRoot);
		return DiagnosticsData.Capture(roots, paths);

		void CollectScopes(Node node)
		{
			if (node is InjectraScope { PreparedContainer: { } container } scope
			    && scope.DiagnosticsRootScope?.PreparedContainer is { } rootContainer)
			{
				if (!paths.TryGetValue(rootContainer, out var rootPaths))
					paths.Add(rootContainer, rootPaths = []);

				// IDs are capture-local, so each independent DI root has its own path table.
				rootPaths.Add(container.GetDiagnostics().Id, scope.GetPath().ToString());
				if (scope.DiagnosticsRoot is not null)
					roots.Add((scope.GetPath().ToString(), container));
			}

			foreach (var child in node.GetChildren(includeInternal: true))
				CollectScopes(child);
		}
	}
}

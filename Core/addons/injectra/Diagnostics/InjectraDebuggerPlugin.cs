#nullable enable

#if TOOLS
using System.Collections.Generic;
using Godot;
using VariantArray = Godot.Collections.Array;

namespace Injectra.Godot.Diagnostics;

/// <summary>Owns a separate inspection panel for each editor debugging session.</summary>
[Tool]
public partial class InjectraDebuggerPlugin : EditorDebuggerPlugin
{
	private readonly Dictionary<int, DiagnosticsPanel> _panels = [];

	public override bool _HasCapture(string capture) => capture == "injectra";

	public override void _SetupSession(int sessionId)
	{
		var session = GetSession(sessionId);
		using var scene = GD.Load<PackedScene>("res://addons/injectra/Diagnostics/DiagnosticsPanel.tscn");
		var panel = scene.Instantiate<DiagnosticsPanel>();
		_panels[sessionId] = panel;
		panel.RefreshRequested = () => session.SendMessage("injectra:refresh", []);
		session.AddSessionTab(panel);
		session.Started += panel.SessionStarted;
		session.Stopped += panel.SessionStopped;
		panel.SetActive(session.IsActive());
	}

	internal void ReleasePanels()
	{
		foreach (var (id, panel) in _panels)
		{
			var session = GetSession(id);
			session.Started -= panel.SessionStarted;
			session.Stopped -= panel.SessionStopped;
			session.RemoveSessionTab(panel);
			panel.RefreshRequested = null;
			panel.QueueFree();
		}
		_panels.Clear();
	}

	public override bool _Capture(string message, VariantArray data, int sessionId)
	{
		if (message != "injectra:snapshot" || !_panels.TryGetValue(sessionId, out var panel))
			return false;

		panel.ShowSnapshot(data);
		return true;
	}
}
#endif

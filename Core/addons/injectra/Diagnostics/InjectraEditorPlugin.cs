#if TOOLS
using Godot;

namespace Injectra.Godot.Diagnostics;

/// <summary>Installs the editor debugger tab and its runtime message receiver.</summary>
[Tool]
public partial class InjectraEditorPlugin : EditorPlugin
{
	private InjectraDebuggerPlugin _debugger = null!;

	public override void _EnterTree()
	{
		_debugger = new InjectraDebuggerPlugin();
		AddDebuggerPlugin(_debugger);
		if (!ProjectSettings.HasSetting("autoload/InjectraDiagnostics"))
			AddAutoloadSingleton("InjectraDiagnostics", "res://addons/injectra/Diagnostics/DiagnosticsBridge.cs");
	}

	public override void _ExitTree()
	{
		_debugger.ReleasePanels();
		RemoveDebuggerPlugin(_debugger);
		_debugger.Dispose();
	}

	public override void _DisablePlugin() => RemoveAutoloadSingleton("InjectraDiagnostics");
}
#endif

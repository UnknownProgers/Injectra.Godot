#nullable enable

#if TOOLS
using Godot;

namespace Injectra.Godot.Diagnostics;

/// <summary>Installs the editor debugger tab and its runtime message receiver.</summary>
[Tool]
public partial class InjectraEditorPlugin : EditorPlugin, ISerializationListener
{
	private InjectraDebuggerPlugin? _debugger;

	public override void _EnterTree()
	{
		InstallDebugger();
		if (!ProjectSettings.HasSetting("autoload/InjectraDiagnostics"))
			AddAutoloadSingleton("InjectraDiagnostics", "res://addons/injectra/Diagnostics/DiagnosticsBridge.cs");
	}

	public override void _ExitTree() => RemoveDebugger();

	// Managed callbacks and dictionaries cannot survive a tool assembly reload.
	public void OnBeforeSerialize() => RemoveDebugger();

	public void OnAfterDeserialize() => InstallDebugger();

	private void InstallDebugger()
	{
		if (_debugger is not null)
			return;

		_debugger = new InjectraDebuggerPlugin();
		AddDebuggerPlugin(_debugger);
	}

	private void RemoveDebugger()
	{
		if (_debugger is null)
			return;

		_debugger.ReleasePanels();
		RemoveDebuggerPlugin(_debugger);
		_debugger.Dispose();
		_debugger = null;
	}

	public override void _DisablePlugin() => RemoveAutoloadSingleton("InjectraDiagnostics");
}
#endif

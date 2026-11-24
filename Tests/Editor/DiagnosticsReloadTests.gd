@tool
extends SceneTree

# Run with Godot --headless --editor --path Core --script ../Tests/Editor/DiagnosticsReloadTests.gd.
# A separate GDScript driver survives the C# assembly reload being tested.
func _initialize():
	_run.call_deferred()

func _run():
	await create_timer(2.0).timeout
	for cycle in range(3):
		EditorInterface.play_main_scene()
		var connected = await _wait_for(func():
			var panel = _find_panel(EditorInterface.get_base_control())
			return panel != null and not panel.get("RefreshButton").disabled)
		if not _check(connected, "The diagnostics session did not connect."):
			return

		var panel = _find_panel(EditorInterface.get_base_control())
		panel.get("RefreshButton").emit_signal("pressed")
		var captured = await _wait_for(func(): return not panel.get("CopyButton").disabled)
		if not _check(captured, "Refresh did not receive a snapshot on run %d." % (cycle + 1)):
			return
		if not _check(panel.get("Entries").get_root().get_first_child() != null,
			"The received snapshot must contain containers."):
			return

		EditorInterface.stop_playing_scene()
		var stopped = await _wait_for(func(): return panel.get("RefreshButton").disabled)
		if not _check(stopped, "Stopping the game must deactivate its diagnostics panel."):
			return

		if cycle < 2:
			var output = []
			var result = OS.execute("dotnet", ["build", ProjectSettings.globalize_path("res://Core.csproj"),
				"--no-restore", "--configuration", "Debug", "-t:Rebuild", "--verbosity", "quiet"], output)
			if not _check(result == 0, "C# rebuild failed: %s" % str(output)):
				return
			var replaced = await _wait_for(func(): return not is_instance_valid(panel))
			if not _check(replaced, "Assembly reload must replace the old diagnostics panel."):
				return

	print("PASS: diagnostics Refresh across three game runs and two C# assembly reloads.")
	quit()

func _wait_for(condition: Callable) -> bool:
	var deadline = Time.get_ticks_msec() + 10000
	while Time.get_ticks_msec() < deadline:
		if condition.call():
			return true
		await create_timer(0.1).timeout
	return false

func _check(condition: bool, message: String) -> bool:
	if not condition:
		push_error(message)
		EditorInterface.stop_playing_scene()
		quit(1)
	return condition

func _find_panel(node: Node):
	if node.has_method("RequestRefresh"):
		return node
	for child in node.get_children():
		var result = _find_panel(child)
		if result != null:
			return result
	return null

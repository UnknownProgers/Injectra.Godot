using System;
using System.Collections.Generic;
using Godot;
using VariantArray = Godot.Collections.Array;

namespace Injectra.Godot.Diagnostics;

/// <summary>Read-only editor view of a snapshot received from the running game.</summary>
[Tool]
public partial class DiagnosticsPanel : VBoxContainer
{
	[Export] public string[] ColumnTitles { get; set; } = [];
	[Export] public int[] MinimumColumnWidths { get; set; } = [];
	[Export] public Tree Entries { get; set; } = null!;
	[Export] public TextEdit Details { get; set; } = null!;
	[Export] public Label Status { get; set; } = null!;
	[Export] public Button RefreshButton { get; set; } = null!;
	[Export] public Button CopyButton { get; set; } = null!;

	internal Action? RefreshRequested { get; set; }
	internal string SnapshotText { get; private set; } = "";

	public override void _Ready()
	{
		for (var i = 0; i < Math.Min(Entries.Columns, ColumnTitles.Length); i++)
			Entries.SetColumnTitle(i, ColumnTitles[i]);
		for (var i = 0; i < Math.Min(Entries.Columns, MinimumColumnWidths.Length); i++)
			Entries.SetColumnCustomMinimumWidth(i, MinimumColumnWidths[i]);
	}

	internal void SessionStarted() => SetActive(true);
	internal void SessionStopped() => SetActive(false);

	internal void SetActive(bool active)
	{
		RefreshButton.Disabled = !active;
		CopyButton.Disabled = true;
		SnapshotText = "";
		Entries.Clear();
		Details.Text = "";
		Status.Text = active ? "Game connected. Press Refresh to inspect live containers." : "Run the project from the editor to inspect Injectra.";
	}

	public void RequestRefresh()
	{
		Status.Text = "Waiting for runtime snapshot. If execution is paused, resume the game.";
		RefreshRequested?.Invoke();
	}

	internal void ShowSnapshot(VariantArray data)
	{
		SnapshotText = data[0].AsString();
		var rows = data[1].AsGodotArray();
		Entries.Clear();
		Details.Text = "";
		var root = Entries.CreateItem();
		var items = new List<TreeItem>();
		foreach (var value in rows)
		{
			var row = value.AsGodotArray();
			var parent = row[0].AsInt32();
			var item = Entries.CreateItem(parent < 0 ? root : items[parent]);
			for (var column = 0; column < 4; column++)
			{
				var text = row[column + 1].AsString();
				item.SetText(column, text);
				item.SetTooltipText(column, text);
			}
			item.SetMetadata(0, row[5]);
			items.Add(item);
		}
		CopyButton.Disabled = rows.Count == 0;
		Status.Text = rows.Count == 0 ? "No active Injectra root scopes found." : $"Snapshot captured · {rows.Count} rows. Select a row for details. Refresh to update.";
	}

	public void UpdateSelection() => Details.Text = Entries.GetSelected()?.GetMetadata(0).AsString() ?? "";

	public void CopySnapshot() => DisplayServer.ClipboardSet(SnapshotText);
}

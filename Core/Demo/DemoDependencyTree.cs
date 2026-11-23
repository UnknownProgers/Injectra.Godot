using Godot;

namespace Injectra.Godot.Demo;

[Tool]
public partial class DemoDependencyTree : Tree
{
	private string[] _titles = [];
	private int[] _widths = [];

	[Export]
	public string[] ColumnTitles
	{
		get => _titles;
		set
		{
			_titles = value;
			ApplyColumns();
		}
	}

	[Export]
	public int[] MinimumColumnWidths
	{
		get => _widths;
		set
		{
			_widths = value;
			ApplyColumns();
		}
	}

	public override void _Ready() => ApplyColumns();

	private void ApplyColumns()
	{
		for (var column = 0; column < Math.Min(Columns, _titles.Length); column++)
			SetColumnTitle(column, _titles[column]);
		for (var column = 0; column < Math.Min(Columns, _widths.Length); column++)
			SetColumnCustomMinimumWidth(column, _widths[column]);
	}
}

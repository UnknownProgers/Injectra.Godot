using Godot;

namespace Injectra.Godot.Tests;

public partial class OrderProbe : Node
{
	public List<string>? Events { get; set; }
	public object? Dependency { get; private set; }

	// A timing probe, not the addon injector: stage 1 only checks Godot's callbacks.
	public void Construct(object dependency)
	{
		Dependency = dependency;
		Events?.Add($"{Name}:Construct");
	}

	public override void _EnterTree() => Record("EnterTree");
	public override void _Ready() => Record("Ready");
	public override void _ExitTree() => Record("ExitTree");

	private void Record(string callback)
	{
		if (Dependency is null)
			throw new InvalidOperationException($"{Name}.{callback} has no dependency.");
		Events?.Add($"{Name}:{callback}");
	}
}

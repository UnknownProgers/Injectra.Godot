namespace Injectra.Godot.Tests;

public partial class GeneratedDerivedProbe : InjectionNode
{
	public int AdditionalCalls { get; private set; }

	[Inject]
	public void Additional() => AdditionalCalls++;
}

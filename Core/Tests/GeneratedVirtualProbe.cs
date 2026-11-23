using Godot;

namespace Injectra.Godot.Tests;

public partial class GeneratedVirtualProbe : Node
{
	public InjectionDependency? Dependency { get; protected set; }
	public int Calls { get; protected set; }

	[Inject]
	public virtual void Construct(InjectionDependency dependency) => throw new InvalidOperationException("The override must replace this body.");
}

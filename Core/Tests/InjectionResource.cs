using Godot;

namespace Injectra.Godot.Tests;

public partial class InjectionResource : Resource
{
	public InjectionDependency? Dependency { get; private set; }

	[Inject]
	public void Construct(InjectionDependency dependency) => Dependency = dependency;
}

namespace Injectra.Godot.Tests;

public partial class GeneratedMarkedOverrideProbe : GeneratedVirtualProbe
{
	[Inject]
	public override void Construct(InjectionDependency dependency)
	{
		Dependency = dependency;
		Calls++;
	}
}

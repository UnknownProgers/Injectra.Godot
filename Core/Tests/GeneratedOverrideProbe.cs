namespace Injectra.Godot.Tests;

public partial class GeneratedOverrideProbe : GeneratedVirtualProbe
{
	public override void Construct(InjectionDependency dependency)
	{
		Dependency = dependency;
		Calls++;
	}
}

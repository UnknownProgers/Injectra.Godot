namespace Injectra.Godot.Tests;

public partial class ScopeInstallerProbe : ResourceInstaller
{
	public InjectionDependency Dependency { get; } = new();
	public override void Install(ContainerBuilder builder) => builder.RegisterInstance(Dependency);
}

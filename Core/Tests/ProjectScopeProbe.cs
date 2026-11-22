namespace Injectra.Godot.Tests;

public partial class ProjectScopeProbe : InjectraProjectScope
{
	public Action<ContainerBuilder>? ConfigureRegistrations { get; set; }
	protected override void Configure(ContainerBuilder builder) => ConfigureRegistrations?.Invoke(builder);
}

using Godot;

namespace Injectra.Godot.Tests;

public partial class ScopeProbe : InjectraScope
{
	public Action<ContainerBuilder>? ConfigureRegistrations { get; set; }
	public InjectionDependency? Dependency { get; private set; }
	public SceneFactory? Factory { get; private set; }
	public bool ReadyWithDependency { get; private set; }
	public int Injections { get; private set; }

	protected override void Configure(ContainerBuilder builder) => ConfigureRegistrations?.Invoke(builder);

	[Inject]
	public void Construct(InjectionDependency dependency, SceneFactory factory)
	{
		Injections++;
		Dependency = dependency;
		Factory = factory;
	}

	public override void _Ready() => ReadyWithDependency = Dependency is not null && Factory is not null;
}

using Godot;

namespace Injectra.Godot.Demo;

[GlobalClass]
public partial class DemoProjectInstaller : ResourceInstaller
{
	public override void Install(ContainerBuilder builder) =>
		builder.Register<DemoProjectSession>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
}

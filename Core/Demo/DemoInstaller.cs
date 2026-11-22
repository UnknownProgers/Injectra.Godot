using Godot;

namespace Injectra.Godot.Demo;

[GlobalClass]
public partial class DemoInstaller : ResourceInstaller
{
	public override void Install(ContainerBuilder builder)
	{
		builder.Register<DemoSession>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
	}
}

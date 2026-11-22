using Godot;

namespace Injectra.Godot.Demo;

[GlobalClass]
public partial class DemoChildInstaller : ResourceInstaller
{
	// Adds a local service without registering DemoSession again.
	public override void Install(ContainerBuilder builder) =>
		builder.Register<DemoChildState>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
}

using Godot;

namespace Injectra.Godot.Demo;

[GlobalClass]
public partial class DemoInstaller : ResourceInstaller
{
	[Export] public string SessionLabel { get; set; } = "Demo session";
	[Export(PropertyHint.Range, "1,999,1")] public int StartingHealth { get; set; } = 100;

	public override void Install(ContainerBuilder builder)
	{
		builder.RegisterInstance(new DemoSettings(SessionLabel, StartingHealth));
		builder.Register<DemoSession>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
	}
}

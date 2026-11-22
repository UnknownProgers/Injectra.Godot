namespace Injectra.Godot.Demo;

public partial class DemoChildScope : InjectraScope
{
	public DemoSession Session { get; private set; } = null!;
	public DemoProjectSession Project { get; private set; } = null!;
	public SceneFactory Scenes { get; private set; } = null!;

	[Inject]
	public void Construct(DemoSession session, DemoProjectSession project, SceneFactory scenes)
	{
		Session = session;
		Project = project;
		Scenes = scenes;
	}
}

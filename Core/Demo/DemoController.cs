using Godot;

namespace Injectra.Godot.Demo;

public partial class DemoController : VBoxContainer
{
	[Export] public PackedScene ActorScene { get; set; } = null!;

	private SceneFactory _scenes = null!;
	private DemoSession _session = null!;
	private DemoProjectSession _project = null!;
	private readonly VBoxContainer _actors = new();
	private readonly Label _status = new();

	[Inject]
	public void Construct(SceneFactory scenes, DemoSession session, DemoProjectSession project)
	{
		_scenes = scenes;
		_session = session;
		_project = project;
	}

	public override void _Ready()
	{
		AddChild(new Label { Text = "Injectra.Godot — Scope & SceneFactory" });
		AddChild(new Label { Text = $"Autoload project session: {_project.Id}" });
		AddChild(new Label { Text = "ResourceInstaller → Container → [Inject] → EnterTree" });
		AddChild(_status);
		var button = new Button { Text = "Create injected actor" };
		button.Pressed += Spawn;
		AddChild(button);
		AddChild(_actors);
		Spawn();
		GD.Print("PASS: demo scope, inspector installer, injected factory and actor Ready.");
	}

	private void Spawn()
	{
		_scenes.Instantiate<DemoActor>(ActorScene, _actors);
		_status.Text = $"One scope, one DemoSession, {_session.Spawned} actors. No manual factory or injector.";
	}
}

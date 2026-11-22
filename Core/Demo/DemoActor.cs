using Godot;

namespace Injectra.Godot.Demo;

public partial class DemoActor : PanelContainer
{
	private DemoSession _session = null!;
	private DemoProjectSession _project = null!;

	[Inject]
	public void Construct(DemoSession session, DemoProjectSession project)
	{
		_session = session;
		_project = project;
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		AddChild(new Label
		{
			Text = $"Actor {_session.NextActor()}: injected before _EnterTree; shared DemoSession.\n"
				+ $"Project lifetime session: {_project.Id}"
		});
	}
}

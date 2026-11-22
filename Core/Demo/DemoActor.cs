using Godot;

namespace Injectra.Godot.Demo;

public partial class DemoActor : PanelContainer
{
	private DemoSession _session = null!;

	[Inject]
	public void Construct(DemoSession session) => _session = session;

	public override void _EnterTree()
	{
		base._EnterTree();
		AddChild(new Label { Text = $"Actor {_session.NextActor()}: injected before _EnterTree; shared DemoSession." });
	}
}

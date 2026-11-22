using Godot;

namespace Injectra.Godot.Demo;

public partial class DemoActor : Node
{
	private DemoSession _session = null!;
	private DemoProjectSession _project = null!;
	public DemoSession Session => _session;
	public DemoProjectSession Project => _project;
	public int Number { get; private set; }

	[Inject]
	public void Construct(DemoSession session, DemoProjectSession project)
	{
		_session = session;
		_project = project;
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		Number = _session.NextActor();
	}
}

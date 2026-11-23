using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Injectra.Godot.Demo;

public partial class DemoController : VBoxContainer
{
	[Export] public PackedScene ChildScopeScene { get; set; } = null!;
	[Export] public PackedScene InheritedChildScopeScene { get; set; } = null!;

	[Export] public Color ProjectColor { get; set; } = Colors.White;
	[Export] public Color SceneColor { get; set; } = Colors.White;
	[Export] public DemoDependencyTree DependencyTree { get; set; } = null!;
	[Export] public Label StatusLabel { get; set; } = null!;
	[Export] public OptionButton InstallerMode { get; set; } = null!;
	[Export] public Button CloseButton { get; set; } = null!;

	private SceneFactory _scenes = null!;
	private DemoSession _session = null!;
	private DemoProjectSession _project = null!;
	private InjectraScope _scope = null!;
	private readonly Dictionary<int, Branch> _branches = [];
	private TreeItem _projectSessionRow = null!;
	private int _nextScope;
	private double _refreshElapsed;

	[Inject]
	public void Construct(SceneFactory scenes, DemoSession session, DemoProjectSession project, InjectraScope scope)
	{
		_scenes = scenes;
		_session = session;
		_project = project;
		_scope = scope;
	}

	public override void _Ready()
	{
		var initialStatus = StatusLabel.Text;

		var projectRow = Row(null, "ProjectScope · Autoload", "", "Application lifetime", ProjectColor);
		_projectSessionRow = Row(projectRow, "DemoProjectSession", _project.Id, "ProjectScope singleton", ProjectColor);
		for (var column = 0; column < 4; column++)
		{
			projectRow.SetSelectable(column, false);
			_projectSessionRow.SetSelectable(column, false);
		}
		var sceneRow = Row(projectRow, $"{_scope.Name} · scene scope", "", "Child of ProjectScope", SceneColor);
		sceneRow.SetMetadata(0, 0);
		var sessionRow = Row(sceneRow, "DemoSession", _session.Id, "Local singleton · DemoInstaller", SceneColor);
		SettingsRow(sceneRow, _session.Settings, "DemoInstaller · inspector values");
		Row(sceneRow, "DemoProjectSession", _project.Id, "Inherited from ProjectScope", ProjectColor);
		Row(sceneRow, "SceneFactory", "", "Local factory", SceneColor);
		var scene = new Branch(0, -1, _scope, _session, _scenes, sceneRow, sessionRow);
		_branches.Add(0, scene);
		Spawn(scene);
		CreateChildScope(scene, 0, select: false);
		CreateChildScope(scene, 1, select: false);
		sceneRow.Select(0);
		StatusLabel.Text = initialStatus;
		Refresh();
		GD.Print("PASS: demo scope, inspector installer, injected factory and actor Ready.");
	}

	public override void _Process(double delta)
	{
		_refreshElapsed += delta;
		if (_refreshElapsed < 0.1) return;
		_refreshElapsed = 0;
		Refresh();
	}

	private void CreateChildScope(Branch parent, int mode, bool select = true)
	{
		var scene = mode == 0 ? ChildScopeScene : InheritedChildScopeScene;
		var scope = parent.Scenes.Instantiate<DemoChildScope>(scene, ActorParent(parent));
		var id = ++_nextScope;
		scope.Name = $"ChildScope{id}";
		var description = mode == 0 ? "same installer" : "different installer";
		var row = Row(parent.Row, $"{scope.Name} · {description}", "", $"Child of {parent.Scope.Name}", SceneColor);
		row.SetMetadata(0, id);
		var inherited = ReferenceEquals(scope.Session, parent.Session);
		var state = scope.Container.ResolveAll<DemoChildState>().SingleOrDefault();
		TreeItem? stateRow = null;
		if (state is not null)
			stateRow = Row(row, "DemoChildState", state.Id, mode == 1 ? "Local · DemoChildInstaller" : $"Inherited from {parent.Scope.Name}", SceneColor);
		var source = inherited ? $"Inherited from {parent.Scope.Name}" : "Local override · DemoInstaller";
		var sessionRow = Row(row, "DemoSession", scope.Session.Id, source, inherited ? ProjectColor : SceneColor);
		SettingsRow(row, scope.Session.Settings, inherited ? $"Inherited from {parent.Scope.Name}" : "DemoInstaller · inspector values");
		Row(row, "DemoProjectSession", scope.Project.Id, "Inherited from ProjectScope", ProjectColor);
		Row(row, "SceneFactory", "", "Local factory · child container", SceneColor);
		var branch = new Branch(id, parent.Id, scope, scope.Session, scope.Scenes, row, sessionRow, state, stateRow);
		_branches.Add(id, branch);
		Spawn(branch);
		if (select) row.Select(0);
		var initialization = scope.Session.IsInitialized && (state is null || state.IsInitialized)
			? "initialized"
			: "initialization deferred until Tick ends";
		StatusLabel.Text = $"{scope.Name} under {parent.Scope.Name}: {description}; session {scope.Session.Id} "
			+ $"{(inherited ? "inherited (same instance)" : "registered locally (new instance)")}; {initialization}.";
		Refresh();
	}

	public void QueueChild()
	{
		var parent = SelectedBranch();
		var mode = InstallerMode.Selected;
		parent.Session.OnNextFrame = () =>
		{
			if (IsInstanceValid(parent.Scope)) CreateChildScope(parent, mode);
			else StatusLabel.Text = "Child creation cancelled: the selected parent was closed.";
		};
		StatusLabel.Text = $"Queued child creation under {parent.Scope.Name} for its session's next FrameTick.";
	}

	public void CreateSelectedChild() => CreateChildScope(SelectedBranch(), InstallerMode.Selected);

	public void SpawnSelected() => Spawn(SelectedBranch());

	private Node ActorParent(Branch branch) => branch.Id == 0 ? this : branch.Scope;

	private void Spawn(Branch branch)
	{
		var actor = branch.Scenes.Instantiate<DemoActor>(ActorParent(branch));
		if (!ReferenceEquals(actor.Session, branch.Session) || !ReferenceEquals(actor.Project, _project)
		    || !ReferenceEquals(actor.Settings, branch.Session.Settings))
			throw new InvalidOperationException("Actor dependencies do not match the selected scope.");
		var row = Row(branch.Row, $"Actor #{actor.Number}", $"Scene: {actor.Session.Id}", $"Project: {actor.Project.Id}", Colors.White);
		row.SetText(3, $"HP {actor.Health} · {actor.Settings.SessionLabel}");
		row.SetTooltipText(3, "Health was initialized in EnterTree from settings injected by the scene factory.");
		row.SetMetadata(0, branch.Id);
		StatusLabel.Text = $"Actor #{actor.Number}: {actor.Health} HP from DemoInstaller.StartingHealth; "
			+ $"session {branch.Session.Id} from {branch.Scope.Name}, project {_project.Id}.";
	}

	public void CloseSelected()
	{
		var branch = SelectedBranch();
		if (branch.Id == 0) return;
		var closed = new List<Branch> { branch };
		for (var i = 0; i < closed.Count; i++)
			closed.AddRange(_branches.Values.Where(candidate => candidate.ParentId == closed[i].Id));
		branch.Scope.Free();
		branch.Row.Free();
		foreach (var entry in closed) _branches.Remove(entry.Id);
		_branches[branch.ParentId].Row.Select(0);
		StatusLabel.Text = $"Closed {closed.Count} scope(s). Session {branch.Session.Id}: disposed={branch.Session.IsDisposed}; "
			+ $"child state disposed={branch.State?.IsDisposed.ToString() ?? "n/a"}. Inherited sessions survive; owned services stop. Project remains alive.";
		Refresh();
	}

	private void Refresh()
	{
		_projectSessionRow.SetText(3, Lifecycle(_project.IsInitialized, _project.Frames, _project.PhysicsFrames));
		foreach (var branch in _branches.Values)
		{
			var session = branch.Session;
			branch.SessionRow.SetText(3, Lifecycle(session.IsInitialized, session.Frames, session.PhysicsFrames));
			if (branch.State is { } state)
				branch.StateRow!.SetText(3, Lifecycle(state.IsInitialized, state.Frames, state.PhysicsFrames));
		}
		UpdateSelection();
	}

	private Branch SelectedBranch()
	{
		var metadata = DependencyTree.GetSelected()?.GetMetadata(0) ?? default;
		var id = metadata.VariantType == Variant.Type.Int ? metadata.AsInt32() : 0;
		return _branches.GetValueOrDefault(id) ?? _branches[0];
	}

	public void UpdateSelection() => CloseButton.Disabled = SelectedBranch().Id == 0;
	private static string Lifecycle(bool initialized, int frames, int physics) => $"Init: {initialized} · F: {frames} · P: {physics}";

	private TreeItem Row(TreeItem? parent, string title, string instance, string owner, Color color)
	{
		var row = DependencyTree.CreateItem(parent);
		row.SetText(0, title);
		row.SetText(1, instance);
		row.SetText(2, owner);
		row.SetCustomColor(0, color);
		row.SetCustomColor(1, color);
		if (parent is not null && parent.GetMetadata(0).VariantType == Variant.Type.Int)
			row.SetMetadata(0, parent.GetMetadata(0));
		return row;
	}

	private void SettingsRow(TreeItem parent, DemoSettings settings, string source)
	{
		Row(parent, "DemoSettings · [Export]", $"{settings.SessionLabel} · HP {settings.StartingHealth}", source, SceneColor);
	}

	private sealed record Branch(
		int Id,
		int ParentId,
		InjectraScope Scope,
		DemoSession Session,
		SceneFactory Scenes,
		TreeItem Row,
		TreeItem SessionRow,
		DemoChildState? State = null,
		TreeItem? StateRow = null);
}

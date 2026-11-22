using Godot;

namespace Injectra.Godot.Tests;

internal static class ScopeTests
{
	public static void Run(Node parent)
	{
		using var installer = new ScopeInstallerProbe();
		var plainInstaller = new ServiceInstaller();
		var scope = new ScopeProbe { Installers = [installer], ConfigureRegistrations = plainInstaller.Install };
		var child = new InjectionNode();
		scope.AddChild(child);
		Throws<InvalidOperationException>(() => _ = scope.Container);
		parent.AddChild(scope);
		Check(scope.ReadyWithDependency && ReferenceEquals(scope.Dependency, installer.Dependency), "A resource installer must run before root injection and Ready.");
		Check(child.Dependency is null, "A scope must default to root-only injection.");
		Check(ReferenceEquals(scope.Factory, scope.Container.Resolve<SceneFactory>()), "The factory must be registered automatically as a singleton.");
		Check(ReferenceEquals(scope, scope.Container.Resolve<InjectraScope>()), "The current scope must be available through DI.");
		var owned = scope.Container.Resolve<OwnedService>();
		var container = scope.Container;
		scope.Prepare();
		Check(ReferenceEquals(container, scope.Container), "Repeated preparation must not rebuild a live scope.");
		parent.RemoveChild(scope);
		Check(owned.Disposals == 1 && GodotObject.IsInstanceValid(scope), "Leaving the tree must dispose services without freeing the externally owned scope node.");
		Throws<ObjectDisposedException>(() => container.Resolve<SceneFactory>());
		Throws<InvalidOperationException>(() => _ = scope.Container);
		Throws<InvalidOperationException>(() => scope.Prepare());
		scope.Free();
		Check(owned.Disposals == 1, "Freeing an exited scope must not dispose services twice.");
		InjectionNode.Created.Clear();

		scope = new ScopeProbe { Installers = [installer], Injection = SceneInjection.Tree };
		child = new InjectionNode { Name = "Child" };
		scope.AddChild(child);
		parent.AddChild(scope);
		Check(child.ReadyWithDependency && ReferenceEquals(child.Dependency, installer.Dependency), "Tree mode must inject descendants before their Ready callbacks.");
		scope.Free();
		InjectionNode.Created.Clear();

		var root = new InjectraScope { Injection = SceneInjection.Tree, Installers = [installer] };
		var nestedDependency = new InjectionDependency();
		var nested = new ScopeProbe { ConfigureRegistrations = builder => builder.RegisterInstance(nestedDependency) };
		root.AddChild(nested);
		root.Prepare();
		Check(ReferenceEquals(nested.Dependency, nestedDependency) && nested.Injections == 1, "A nested scope must inject once using its own overrides.");
		Check(!ReferenceEquals(nested.Factory, root.Container.Resolve<SceneFactory>()), "Each scope must have its own scene factory.");
		root.Free();

		var disposedOnFailure = new OwnedService();
		scope = new ScopeProbe
		{
			ConfigureRegistrations = builder => builder.Register<OwnedService>(_ => disposedOnFailure, Lifetime.Singleton).NonLazy()
		};
		Throws<InjectionException>(() => scope.Prepare());
		Check(disposedOnFailure.Disposals == 1, "Failed injection must dispose the built container.");
		Throws<InvalidOperationException>(() => _ = scope.Container);
		scope.Free();

		root = new InjectraScope
		{
			Installers = [installer]
		};
		root.Prepare();
		var detachedContainer = root.Container;
		root.Free();
		Throws<ObjectDisposedException>(() => detachedContainer.Resolve<SceneFactory>());

		root = new InjectraScope { Injection = (SceneInjection)42 };
		Throws<ArgumentOutOfRangeException>(() => root.Prepare());
		root.Free();
		root = new InjectraScope { Installers = [null!] };
		Throws<InvalidOperationException>(() => root.Prepare());
		root.Free();
		FactoryPreparesScope(parent, installer);
		DemoSceneWorks(parent);
		GD.Print("PASS: root scope, resource/code installers, automatic factory, injection modes, boundaries and disposal.");
	}

	private static void FactoryPreparesScope(Node parent, ScopeInstallerProbe installer)
	{
		var source = new ScopeProbe { Installers = [installer], Injection = SceneInjection.Tree };
		var child = new InjectionNode { Name = "Child" };
		source.AddChild(child);
		child.Owner = source;
		using var scene = new PackedScene();
		Check(scene.Pack(source) == Error.Ok, "Packing the scope scene failed.");
		source.Free();
		using var outside = new ContainerBuilder().Build();
		var factory = new SceneFactory(outside);
		var instance = factory.Instantiate<ScopeProbe>(scene, parent);
		Check(instance.ReadyWithDependency && instance.Injections == 1, "A scope created by the factory must prepare itself once.");
		Check(instance.GetNode<InjectionNode>("Child").ReadyWithDependency, "A scope root must use its own configured injection mode.");
		instance.Free();

		var hostScope = new InjectraScope();
		instance = factory.Instantiate<ScopeProbe>(scene, hostScope);
		Check(instance.Injections == 1, "A factory must support creating a scope under another scope.");
		hostScope.Free();
		InjectionNode.Created.Clear();
	}

	private static void DemoSceneWorks(Node parent)
	{
		using var scene = GD.Load<PackedScene>("res://main.tscn");
		var instance = scene.Instantiate<InjectraScope>();
		parent.AddChild(instance);
		var session = instance.Container.Resolve<Demo.DemoSession>();
		var controller = instance.GetNode<Demo.DemoController>("DemoController");
		var tree = (Tree)controller.FindChild("DependencyTree", recursive: true, owned: false);
		var actorButton = (Button)controller.FindChild("CreateActor", recursive: true, owned: false);
		var create = (Button)controller.FindChild("CreateChildScope", recursive: true, owned: false);
		var close = (Button)controller.FindChild("CloseChildScope", recursive: true, owned: false);
		var mode = (OptionButton)controller.FindChild("ChildInstaller", recursive: true, owned: false);
		var initial = controller.GetChildren().OfType<Demo.DemoChildScope>().ToArray();
		Check(initial.Length == 2, "The demo must start with both installer examples.");
		var same = initial.Single(scope => scope.Installers[0] is Demo.DemoInstaller);
		var inherited = initial.Single(scope => scope.Installers[0] is Demo.DemoChildInstaller);
		Check(!ReferenceEquals(same.Session, session), "The same installer must register a fresh local session in the child.");
		Check(ReferenceEquals(inherited.Session, session), "The different installer must inherit DemoSession without registering it again.");
		var inheritedState = inherited.Container.Resolve<Demo.DemoChildState>();
		Check(inheritedState.IsInitialized && !ReferenceEquals(same.Scenes, inherited.Scenes), "The child-only service must initialize and each scope must have a local factory.");
		Check(ReferenceEquals(same.Project, inherited.Project), "Both examples must share the project session.");
		Check(session.Spawned == 2, "Actors in the scene and inheriting child must share the session's counter.");
		actorButton.EmitSignal(Button.SignalName.Pressed);
		Check(session.Spawned == 3, "The initial UI selection must target GameScope.");

		SelectScope(tree, same);
		create.EmitSignal(Button.SignalName.Pressed);
		var nested = same.GetChildren().OfType<Demo.DemoChildScope>().Single();
		Check(!ReferenceEquals(nested.Session, same.Session), "A child created under the selection must apply its own installer.");
		var closedSession = nested.Session;
		actorButton.EmitSignal(Button.SignalName.Pressed);
		Check(closedSession.Spawned == 2 && session.Spawned == 3, "Actor creation must use the selected nested scope's factory.");
		close.EmitSignal(Button.SignalName.Pressed);
		Check(closedSession.IsDisposed && !same.Session.IsDisposed, "Closing a nested scope must preserve its parent.");
		Check(!TreeContains(tree.GetRoot(), closedSession.Id), "Closed dependency rows must leave the UI tree.");

		mode.Select(1);
		create.EmitSignal(Button.SignalName.Pressed);
		var nestedInherited = same.GetChildren().OfType<Demo.DemoChildScope>().Single();
		var nestedState = nestedInherited.Container.Resolve<Demo.DemoChildState>();
		Check(ReferenceEquals(nestedInherited.Session, same.Session), "Inheritance must use the selected parent's session, not always GameScope's.");
		Check(TreeContains(tree.GetRoot(), nestedState.Id), "The new child-only dependency must be shown in the UI.");

		SelectScope(tree, same);
		var inTick = (Button)controller.FindChild("CreateScopeInTick", recursive: true, owned: false);
		inTick.EmitSignal(Button.SignalName.Pressed);
		Check(same.Session.OnNextFrame is not null && session.OnNextFrame is null, "Creation during Tick must be queued on the selected context.");
		SelectScope(tree, inherited);
		var frames = session.Frames;
		InjectraProjectScope.Active!._Process(0.01);
		var fromTick = same.GetChildren().OfType<Demo.DemoChildScope>().Single(scope => scope != nestedInherited);
		var tickState = fromTick.Container.Resolve<Demo.DemoChildState>();
		Check(tickState.IsInitialized && tickState.Frames == 0, "A child created during Tick must initialize after traversal and wait for the next phase.");
		Check(session.Frames == frames + 1, "An inherited session must not receive duplicate lifecycle ticks.");

		SelectScope(tree, inherited);
		mode.Select(0);
		create.EmitSignal(Button.SignalName.Pressed);
		var underInherited = inherited.GetChildren().OfType<Demo.DemoChildScope>().Single();
		Check(ReferenceEquals(underInherited.Container.Resolve<Demo.DemoChildState>(), inheritedState), "New scopes must use the selected factory's dependency context.");
		SelectScope(tree, same);
		close.EmitSignal(Button.SignalName.Pressed);
		Check(same.Session.IsDisposed && nestedState.IsDisposed && tickState.IsDisposed, "Closing a parent must dispose all nested owned services.");
		Check(!session.IsDisposed && !inheritedState.IsDisposed && GodotObject.IsInstanceValid(underInherited), "Unrelated branches and inherited parent sessions must survive.");
		Check(!TreeContains(tree.GetRoot(), nestedState.Id) && TreeContains(tree.GetRoot(), inheritedState.Id), "UI removal must match the closed subtree only.");
		SelectScope(tree, inherited);
		close.EmitSignal(Button.SignalName.Pressed);
		Check(!session.IsDisposed && inheritedState.IsDisposed, "Closing an inheriting scope must dispose its local state, not its inherited session.");
		instance.Free();
	}

	private static void SelectScope(Tree tree, Node scope)
	{
		var row = FindScope(tree.GetRoot(), $"{scope.Name} ·")
			?? throw new InvalidOperationException($"Missing UI row for {scope.Name}.");
		row.Select(0);
	}

	private static TreeItem? FindScope(TreeItem row, string prefix)
	{
		if (row.GetText(0).StartsWith(prefix, StringComparison.Ordinal)) return row;
		for (var child = row.GetFirstChild(); child is not null; child = child.GetNext())
			if (FindScope(child, prefix) is { } found) return found;
		return null;
	}

	private static bool TreeContains(TreeItem row, string text)
	{
		for (var column = 0; column < 4; column++)
			if (row.GetText(column).Contains(text, StringComparison.Ordinal)) return true;
		for (var child = row.GetFirstChild(); child is not null; child = child.GetNext())
			if (TreeContains(child, text)) return true;
		return false;
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private static void Throws<T>(Action action) where T : Exception
	{
		try { action(); }
		catch (T) { return; }
		throw new InvalidOperationException($"Expected {typeof(T).Name}.");
	}

	private sealed class ServiceInstaller : IInstaller
	{
		public void Install(ContainerBuilder builder) => builder.Register<OwnedService>(Lifetime.Singleton);
	}

	private sealed class OwnedService : IDisposable
	{
		public int Disposals { get; private set; }
		public void Dispose() => Disposals++;
	}
}

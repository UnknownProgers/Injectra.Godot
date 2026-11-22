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
		Check(session.Spawned == 1, "The demo must create its first injected actor automatically.");
		var controller = instance.GetNode<Demo.DemoController>("DemoController");
		var button = controller.GetChildren().OfType<Button>().Single();
		button.EmitSignal(Button.SignalName.Pressed);
		Check(session.Spawned == 2, "The demo button must create another actor using the injected factory.");
		instance.Free();
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

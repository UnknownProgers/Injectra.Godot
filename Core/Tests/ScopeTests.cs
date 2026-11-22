using Godot;

namespace Injectra.Godot.Tests;

internal static class ScopeTests
{
	public static void Run(Node parent)
	{
		InstallersPrepareRootBeforeReady(parent);
		RootModeDoesNotInjectChildren(parent);
		TreeModeInjectsChildrenBeforeReady(parent);
		NestedScopeUsesItsOwnOverrides();
		LeavingTreeClosesContainerOnce(parent);
		FailedInjectionDisposesOwnedServices();
		FreeingDetachedScopeClosesContainer();
		InvalidConfigurationIsRejected();
		FactoryPreparesScopeBeforeAddingIt(parent);
		FactoryCanCreateScopeUnderAnotherScope();
		DemoSceneTests.Run(parent);

		InjectionNode.Created.Clear();
		GD.Print("PASS: root scope, resource/code installers, automatic factory, injection modes, boundaries and disposal.");
	}

	private static void InstallersPrepareRootBeforeReady(Node parent)
	{
		using var resourceInstaller = new ScopeInstallerProbe();
		var codeInstaller = new ServiceInstaller();
		var scope = new ScopeProbe
		{
			Installers = [resourceInstaller],
			ConfigureRegistrations = codeInstaller.Install
		};

		Throws<InvalidOperationException>(() => _ = scope.Container);
		parent.AddChild(scope);

		Check(scope.ReadyWithDependency, "The root must be injected before Ready.");
		Check(ReferenceEquals(scope.Dependency, resourceInstaller.Dependency), "Resource installer registrations must be available during injection.");
		Check(scope.Container.Resolve<OwnedService>() is not null, "The code installer must register its service.");
		Check(ReferenceEquals(scope.Factory, scope.Container.Resolve<SceneFactory>()), "The factory must be an automatically registered singleton.");
		Check(ReferenceEquals(scope, scope.Container.Resolve<InjectraScope>()), "The current scope must be available through DI.");

		var originalContainer = scope.Container;
		scope.Prepare();
		Check(ReferenceEquals(originalContainer, scope.Container), "Repeated preparation must not rebuild a live scope.");

		scope.Free();
	}

	private static void RootModeDoesNotInjectChildren(Node parent)
	{
		using var installer = new ScopeInstallerProbe();
		var scope = new ScopeProbe { Installers = [installer] };
		var child = new InjectionNode();
		scope.AddChild(child);

		parent.AddChild(scope);

		Check(scope.ReadyWithDependency, "Default mode must inject the root.");
		Check(child.Dependency is null, "Default Root mode must not inject descendants.");
		scope.Free();
	}

	private static void TreeModeInjectsChildrenBeforeReady(Node parent)
	{
		using var installer = new ScopeInstallerProbe();
		var scope = new ScopeProbe { Installers = [installer], Injection = SceneInjection.Tree };
		var child = new InjectionNode { Name = "Child" };
		scope.AddChild(child);

		parent.AddChild(scope);

		Check(child.ReadyWithDependency, "Tree mode must inject the child before Ready.");
		Check(ReferenceEquals(child.Dependency, installer.Dependency), "The child must receive the scope's dependency.");
		scope.Free();
	}

	private static void NestedScopeUsesItsOwnOverrides()
	{
		using var installer = new ScopeInstallerProbe();
		var parentScope = new InjectraScope { Installers = [installer], Injection = SceneInjection.Tree };
		var childDependency = new InjectionDependency();
		var childScope = new ScopeProbe
		{
			ConfigureRegistrations = builder => builder.RegisterInstance(childDependency)
		};
		parentScope.AddChild(childScope);

		parentScope.Prepare();

		Check(ReferenceEquals(childScope.Dependency, childDependency), "The child must use its local override.");
		Check(childScope.Injections == 1, "The parent injector must not inject the child scope a second time.");
		Check(!ReferenceEquals(childScope.Factory, parentScope.Container.Resolve<SceneFactory>()), "Each scope must have its own factory.");
		parentScope.Free();
	}

	private static void LeavingTreeClosesContainerOnce(Node parent)
	{
		using var installer = new ScopeInstallerProbe();
		var serviceScope = new ScopeProbe
		{
			Installers = [installer],
			ConfigureRegistrations = new ServiceInstaller().Install
		};
		parent.AddChild(serviceScope);
		var container = serviceScope.Container;
		var service = container.Resolve<OwnedService>();

		parent.RemoveChild(serviceScope);

		Check(service.Disposals == 1, "Leaving the tree must dispose owned services.");
		Check(GodotObject.IsInstanceValid(serviceScope), "Container disposal must not free the externally owned scope node.");
		Throws<ObjectDisposedException>(() => container.Resolve<SceneFactory>());
		Throws<InvalidOperationException>(() => _ = serviceScope.Container);
		Throws<InvalidOperationException>(() => serviceScope.Prepare());

		serviceScope.Free();
		Check(service.Disposals == 1, "Freeing an exited scope must not dispose services twice.");
	}

	private static void FailedInjectionDisposesOwnedServices()
	{
		var service = new OwnedService();
		var scope = new ScopeProbe
		{
			// ScopeProbe needs InjectionDependency, deliberately missing here.
			ConfigureRegistrations = builder => builder.Register<OwnedService>(_ => service, Lifetime.Singleton).NonLazy()
		};

		Throws<InjectionException>(() => scope.Prepare());

		Check(service.Disposals == 1, "Failed injection must dispose the built container's owned services.");
		Throws<InvalidOperationException>(() => _ = scope.Container);
		scope.Free();
	}

	private static void FreeingDetachedScopeClosesContainer()
	{
		var scope = new InjectraScope();
		scope.Prepare();
		var container = scope.Container;

		scope.Free();

		Throws<ObjectDisposedException>(() => container.Resolve<SceneFactory>());
	}

	private static void InvalidConfigurationIsRejected()
	{
		var invalidMode = new InjectraScope { Injection = (SceneInjection)42 };
		Throws<ArgumentOutOfRangeException>(() => invalidMode.Prepare());
		invalidMode.Free();

		var missingInstaller = new InjectraScope { Installers = [null!] };
		Throws<InvalidOperationException>(() => missingInstaller.Prepare());
		missingInstaller.Free();
	}

	private static void FactoryPreparesScopeBeforeAddingIt(Node parent)
	{
		using var installer = new ScopeInstallerProbe();
		using var scene = CreateScopeScene(installer);
		using var factoryContainer = new ContainerBuilder().Build();
		var factory = new SceneFactory(factoryContainer);

		var scope = factory.Instantiate<ScopeProbe>(scene, parent);

		Check(scope.ReadyWithDependency, "A factory-created scope must have dependencies before Ready.");
		Check(scope.Injections == 1, "EnterTree must not repeat the factory's preparation.");
		Check(scope.GetNode<InjectionNode>("Child").ReadyWithDependency, "The scope must use its own Tree injection setting.");
		scope.Free();
	}

	private static void FactoryCanCreateScopeUnderAnotherScope()
	{
		using var installer = new ScopeInstallerProbe();
		using var scene = CreateScopeScene(installer);
		using var factoryContainer = new ContainerBuilder().Build();
		var factory = new SceneFactory(factoryContainer);
		var parentScope = new InjectraScope();

		var childScope = factory.Instantiate<ScopeProbe>(scene, parentScope);

		Check(childScope.Injections == 1, "Creating a scope under another scope must inject it once.");
		parentScope.Free();
	}

	private static PackedScene CreateScopeScene(ScopeInstallerProbe installer)
	{
		var root = new ScopeProbe { Installers = [installer], Injection = SceneInjection.Tree };
		var child = new InjectionNode { Name = "Child" };
		root.AddChild(child);
		child.Owner = root;

		var scene = new PackedScene();
		Check(scene.Pack(root) == Error.Ok, "Packing the scope test scene failed.");
		root.Free();
		return scene;
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private static void Throws<T>(Action action) where T : Exception
	{
		try
		{
			action();
		}
		catch (T)
		{
			return;
		}

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

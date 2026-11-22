using Godot;

namespace Injectra.Godot.Tests;

internal static class ProjectScopeTests
{
	public static void Run(Node parent)
	{
		var project = parent.GetTree().Root.GetNode<InjectraProjectScope>("ProjectScope");
		Check(ReferenceEquals(project, InjectraProjectScope.Active), "The Autoload must prepare the project scope before the main scene.");
		var projectContainer = project.Container;
		var session = projectContainer.Resolve<Demo.DemoProjectSession>();
		var projectFactory = projectContainer.Resolve<SceneFactory>();
		using var installer = new ScopeInstallerProbe();

		for (var i = 0; i < 2; i++)
		{
			var level = new ScopeProbe
			{
				Installers = [installer],
				ConfigureRegistrations = builder => builder.Register<OwnedService>(Lifetime.Singleton)
			};
			parent.AddChild(level);
			var service = level.Container.Resolve<OwnedService>();
			Check(ReferenceEquals(level.Container.Resolve<Demo.DemoProjectSession>(), session), "Each level must inherit the same project singleton.");
			Check(!ReferenceEquals(projectFactory, level.Factory), "A scene scope must register a local factory.");
			level.Free();
			Check(service.Disposals == 1 && !session.IsDisposed, "Changing levels must dispose level services and preserve project services.");
		}

		NearestAncestorWins(parent, installer);
		FactoryContextWins(parent, installer);

		var duplicate = new InjectraProjectScope();
		Throws<InvalidOperationException>(() => duplicate.Prepare());
		Check(ReferenceEquals(InjectraProjectScope.Active, project), "Rejecting a duplicate must preserve the existing project scope.");
		duplicate.Free();

		var host = new InjectraScope();
		duplicate = new InjectraProjectScope();
		host.AddChild(duplicate);
		Throws<InvalidOperationException>(() => duplicate.Prepare());
		host.Free();

		project.GetParent().RemoveChild(project);
		Check(InjectraProjectScope.Active is null && session.IsDisposed, "Project exit must clear the active scope and dispose project singletons.");
		Throws<ObjectDisposedException>(() => projectContainer.Resolve<SceneFactory>());
		project.Free();

		var standalone = new InjectraScope();
		standalone.Prepare();
		Check(standalone.Container.Resolve<SceneFactory>() is not null, "Scenes must still work without a project scope.");
		Throws<ResolutionException>(() => standalone.Container.Resolve<Demo.DemoProjectSession>());
		standalone.Free();

		FailureAndShutdown();
		GD.Print("PASS: Autoload project lifetime, scene replacement, ancestor overrides, factory context, duplicate rejection and project cleanup.");
	}

	private static void NearestAncestorWins(Node parent, ScopeInstallerProbe installer)
	{
		var ancestor = new InjectraScope { Installers = [installer], Injection = SceneInjection.Tree };
		var localDependency = new InjectionDependency();
		var child = new ScopeProbe
		{
			Injection = SceneInjection.Tree,
			ConfigureRegistrations = builder => builder.RegisterInstance(localDependency)
		};
		var grandchild = new ScopeProbe();
		child.AddChild(grandchild);
		ancestor.AddChild(child);
		parent.AddChild(ancestor);
		Check(ReferenceEquals(child.Dependency, localDependency) && ReferenceEquals(grandchild.Dependency, localDependency), "A grandchild must inherit the nearest scope's override.");
		Check(child.Injections == 1 && grandchild.Injections == 1 && grandchild.ReadyWithDependency, "Nested scope preparation must inject once before Ready.");
		Check(ReferenceEquals(ancestor.Container.Resolve<InjectionDependency>(), installer.Dependency), "Child overrides must not change ancestor registrations.");
		Check(!ReferenceEquals(child.Factory, grandchild.Factory), "A grandchild must also own its scene factory.");
		ancestor.Free();
	}

	private static void FactoryContextWins(Node parent, ScopeInstallerProbe installer)
	{
		var sourceScope = new InjectraScope { Installers = [installer] };
		var placementScope = new InjectraScope();
		parent.AddChild(sourceScope);
		parent.AddChild(placementScope);
		var source = new ScopeProbe();
		using var scene = new PackedScene();
		Check(scene.Pack(source) == Error.Ok, "Packing a scope scene failed.");
		source.Free();
		var instance = sourceScope.Container.Resolve<SceneFactory>().Instantiate<ScopeProbe>(scene, placementScope);
		Check(ReferenceEquals(instance.Dependency, installer.Dependency), "A factory's source container must take precedence over the placement node's scope.");
		Check(ReferenceEquals(instance.Container.Resolve<Demo.DemoProjectSession>(), InjectraProjectScope.Active!.Container.Resolve<Demo.DemoProjectSession>()), "Factory-created scopes must retain project ancestry.");
		instance.Free();
		sourceScope.Free();
		placementScope.Free();
	}

	private static void FailureAndShutdown()
	{
		var failed = new ProjectScopeProbe
		{
			ConfigureRegistrations = _ => throw new InvalidOperationException("Installer failed.")
		};
		Throws<InvalidOperationException>(() => failed.Prepare());
		Check(InjectraProjectScope.Active is null, "Failed project preparation must not leave an active project scope.");
		failed.Free();

		var project = new ProjectScopeProbe
		{
			ConfigureRegistrations = builder => builder.Register<OwnedService>(Lifetime.Singleton)
		};
		project.Prepare();
		var projectService = project.Container.Resolve<OwnedService>();
		var level = new ScopeProbe
		{
			ConfigureRegistrations = builder =>
			{
				builder.RegisterInstance(new InjectionDependency());
				builder.Register<OwnedService>(Lifetime.Singleton);
			}
		};
		level.Prepare();
		var levelContainer = level.Container;
		var levelService = levelContainer.Resolve<OwnedService>();
		project.Free();
		Check(projectService.Disposals == 1 && levelService.Disposals == 1 && InjectraProjectScope.Active is null, "Closing the project must dispose remaining child containers and clear the project reference.");
		Throws<ObjectDisposedException>(() => levelContainer.Resolve<SceneFactory>());
		level.Free();
		Check(levelService.Disposals == 1, "Later node cleanup must not dispose an already closed child twice.");
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

	private sealed class OwnedService : IDisposable
	{
		public int Disposals { get; private set; }
		public void Dispose() => Disposals++;
	}
}

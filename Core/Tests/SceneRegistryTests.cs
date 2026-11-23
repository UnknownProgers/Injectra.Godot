using Godot;

namespace Injectra.Godot.Tests;

internal static class SceneRegistryTests
{
	public static void Run(Node parent)
	{
		CreatesAndInjectsRegisteredScene(parent);
		ChildrenInheritAndOverrideScenes();
		PreparedMappingsDoNotFollowResourceEdits();
		InvalidRegistrationsAreRejected();
		GD.Print("PASS: typed scene creation, registry inheritance, overrides and validation.");
	}

	private static void CreatesAndInjectsRegisteredScene(Node parent)
	{
		using var scene = CreateScene("RegisteredRoot");
		using var entry = Entry(scene);
		using var registry = new InjectraSceneRegistry { Entries = [entry] };
		using var installer = new ScopeInstallerProbe();
		var scope = new InjectraScope { Installers = [installer], SceneRegistry = registry };
		scope.Prepare();
		var factory = scope.Container.Resolve<SceneFactory>();

		var node = factory.Instantiate<InjectionNode>(parent);

		Check(node.ReadyWithDependency, "Registered scenes must be injected before Ready.");
		Check(ReferenceEquals(node.Dependency, installer.Dependency), "Typed creation must use the factory's scope.");
		Check(node.Name == "RegisteredRoot", "The registered scene must be selected.");
		node.Free();

		var detached = factory.Instantiate<InjectionNode>();
		Check(!detached.IsInsideTree() && detached.Dependency is not null, "Creation without a parent must return an injected detached node.");
		detached.Free();
		Throws<InvalidOperationException>(() => factory.Instantiate<ScopeProbe>());
		scope.Free();
	}

	private static void ChildrenInheritAndOverrideScenes()
	{
		using var parentScene = CreateScene("ParentScene");
		using var childScene = CreateScene("ChildScene");
		using var parentEntry = Entry(parentScene);
		using var childEntry = Entry(childScene);
		using var parentRegistry = new InjectraSceneRegistry { Entries = [parentEntry] };
		using var childRegistry = new InjectraSceneRegistry { Entries = [childEntry] };
		using var installer = new ScopeInstallerProbe();
		var root = new InjectraScope { Installers = [installer], SceneRegistry = parentRegistry };
		using var emptyRegistry = new InjectraSceneRegistry();
		var childDependency = new InjectionDependency();
		var inheriting = new ScopeProbe
		{
			SceneRegistry = emptyRegistry,
			ConfigureRegistrations = builder => builder.RegisterInstance(childDependency)
		};
		var overriding = new InjectraScope { SceneRegistry = childRegistry };
		root.AddChild(inheriting);
		root.AddChild(overriding);
		root.Prepare();
		inheriting.Prepare();
		overriding.Prepare();

		var inherited = inheriting.Container.Resolve<SceneFactory>().Instantiate<InjectionNode>();
		var replaced = overriding.Container.Resolve<SceneFactory>().Instantiate<InjectionNode>();
		var original = root.Container.Resolve<SceneFactory>().Instantiate<InjectionNode>();

		Check(ReferenceEquals(inherited.Dependency, childDependency), "Inherited scenes must use child dependencies, not the parent factory resolver.");
		Check(inherited.Name == "ParentScene", "An unregistered child must inherit its parent's scene.");
		Check(replaced.Name == "ChildScene", "A local mapping must override its parent's scene.");
		Check(original.Name == "ParentScene", "A child override must not change the parent registry.");
		inherited.Free();
		replaced.Free();
		original.Free();
		root.Free();
	}

	private static void PreparedMappingsDoNotFollowResourceEdits()
	{
		using var originalScene = CreateScene("Original");
		using var replacementScene = CreateScene("Replacement");
		using var entry = Entry(originalScene);
		using var registry = new InjectraSceneRegistry { Entries = [entry] };
		using var installer = new ScopeInstallerProbe();
		var scope = new InjectraScope { Installers = [installer], SceneRegistry = registry };
		scope.Prepare();

		entry.Scene = replacementScene;
		registry.Entries.Clear();
		var node = scope.Container.Resolve<SceneFactory>().Instantiate<InjectionNode>();

		Check(node.Name == "Original", "Prepared mappings must not change when the resource is edited.");
		node.Free();
		scope.Free();
	}

	private static void InvalidRegistrationsAreRejected()
	{
		using var scene = CreateScene("Valid");
		using var entry = Entry(scene);
		using var duplicate = new InjectraSceneRegistry { Entries = [entry, entry] };
		var scope = new InjectraScope { SceneRegistry = duplicate };
		Throws<InvalidOperationException>(() => scope.Prepare());
		scope.Free();

		using var missing = new InjectraSceneRegistration();
		using var incomplete = new InjectraSceneRegistry { Entries = [missing] };
		var invalidScope = new InjectraScope { SceneRegistry = incomplete };
		Throws<InvalidOperationException>(() => invalidScope.Prepare());
		invalidScope.Free();

		using var wrongScene = new PackedScene();
		var wrongRoot = new Node();
		Check(wrongScene.Pack(wrongRoot) == Error.Ok, "Packing failed.");
		wrongRoot.Free();
		using var wrongEntry = Entry(wrongScene);
		using var wrongRegistry = new InjectraSceneRegistry { Entries = [wrongEntry] };
		var wrongScope = new InjectraScope { SceneRegistry = wrongRegistry };
		wrongScope.Prepare();
		Throws<InvalidCastException>(() => wrongScope.Container.Resolve<SceneFactory>().Instantiate<InjectionNode>());
		wrongScope.Free();
	}

	private static InjectraSceneRegistration Entry(PackedScene scene) => new()
	{
		Script = GD.Load<CSharpScript>("res://Tests/InjectionNode.cs"),
		Scene = scene
	};

	private static PackedScene CreateScene(string name)
	{
		var root = new InjectionNode { Name = name };
		var scene = new PackedScene();
		Check(scene.Pack(root) == Error.Ok, "Packing failed.");
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
}

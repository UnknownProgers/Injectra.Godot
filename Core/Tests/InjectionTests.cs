using Godot;

namespace Injectra.Godot.Tests;

internal static class InjectionTests
{
	public static void Run(Node parent)
	{
		var dependency = new InjectionDependency();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(dependency);
		using var container = builder.Build();
		var injector = new MethodInjector(container);

		var derived = new DerivedTarget();
		injector.Inject(derived);
		injector.Inject(derived);
		Check(derived.Calls.SequenceEqual(["Base", "Derived", "Base", "Derived"]), "Base-first injection or repeated injection failed.");
		Check(ReferenceEquals(derived.Dependency, dependency), "Method arguments must come from the supplied resolver.");

		var overridden = new OverrideTarget();
		injector.Inject(overridden);
		Check(overridden.Calls == 1, "An inherited virtual injection method must run once.");
		var markedOverride = new MarkedOverrideTarget();
		injector.Inject(markedOverride);
		Check(markedOverride.Calls == 1, "Repeating Inject on an override must not duplicate its call.");
		var invalidDerived = new DuplicateDerivedTarget();
		Throws<InjectionException>(() => injector.Inject(invalidDerived));
		Check(invalidDerived.Calls.Count == 0, "Duplicate derived methods must fail before calling a valid base method.");
		foreach (var invalidOverride in new VirtualTarget[] { new OverrideAndAdditional(), new MarkedOverrideAndAdditional() })
		{
			Throws<InjectionException>(() => injector.Inject(invalidOverride));
			Check(invalidOverride.Calls == 0, "An override plus another injection method is a duplicate, regardless of the repeated attribute.");
		}
		var multiple = new MultipleTarget();
		Throws<InjectionException>(() => injector.Inject(multiple));
		Check(multiple.Calls == 0, "Multiple injection methods in one class must fail before any calls.");
		var partiallyInvalid = new PartiallyInvalidTarget();
		Throws<InjectionException>(() => injector.Inject(partiallyInvalid));
		Check(partiallyInvalid.Calls == 0, "All method signatures must be validated before invoking the first method.");

		foreach (var invalid in new object[] { new PrivateTarget(), new StaticTarget(), new ReturnTarget(), new GenericTarget(), new RefTarget(), new AsyncTarget() })
		{
			var error = Throws<InjectionException>(() => injector.Inject(invalid));
			Check(error.Message.Contains(invalid.GetType().Name), "Invalid-method diagnostics must identify the declaring type.");
		}
		var missing = Throws<InjectionException>(() => injector.Inject(new MissingTarget()));
		Check(missing.InnerException is ResolutionException && missing.Message.Contains(nameof(Unregistered)), "Missing dependencies must identify their contract and preserve the resolution error.");
		var thrown = Throws<InjectionException>(() => injector.Inject(new ThrowingTarget()));
		Check(thrown.InnerException is InvalidOperationException { Message: "User error" }, "User exceptions must retain their original cause.");
		Throws<ArgumentNullException>(() => new MethodInjector(null!));
		Throws<ArgumentNullException>(() => injector.Inject(null!));
		Throws<ArgumentNullException>(() => injector.InjectTree(null!));

		using var resource = new InjectionResource();
		var holder = new ResourceHolder(resource);
		injector.Inject(holder);
		Check(resource.Dependency is null, "Referenced resources must not be injected implicitly.");
		injector.Inject(resource);
		Check(ReferenceEquals(resource.Dependency, dependency), "Explicit resource injection failed.");

		var freed = new Node();
		freed.Free();
		Throws<ObjectDisposedException>(() => injector.Inject(freed));
		SceneCreation(parent, container, dependency);
		GD.Print("PASS: method injection, inheritance, validation, resources, scene creation and failed-instance cleanup.");
	}

	private static void SceneCreation(Node parent, IResolver resolver, InjectionDependency dependency)
	{
		var source = new InjectionNode { Name = "Root" };
		var child = new InjectionNode { Name = "Child" };
		source.AddChild(child);
		child.Owner = source;
		using var scene = new PackedScene();
		Check(scene.Pack(source) == Error.Ok, "Packing the test scene failed.");
		source.Free();
		InjectionNode.Created.Clear();

		var factory = new SceneFactory(resolver);
		dependency.Events.Clear();
		var instance = factory.Instantiate<InjectionNode>(scene, parent);
		var instanceChild = instance.GetNode<InjectionNode>("Child");
		Check(instance.ReadyWithDependency && !instanceChild.ReadyWithDependency && instanceChild.Dependency is null, "Default injection must affect only the scene root.");
		Check(dependency.Events.SequenceEqual(["Root:Inject", "Root:EnterTree", "Root:Ready"]), "Default injection must finish before the root enters the tree.");
		instance.Free();
		InjectionNode.Created.Clear();

		dependency.Events.Clear();
		instance = factory.Instantiate<InjectionNode>(scene, parent, injection: SceneInjection.Tree);
		instanceChild = instance.GetNode<InjectionNode>("Child");
		Check(instance.ReadyWithDependency && instanceChild.ReadyWithDependency, "Every node must be injected before Ready.");
		Check(dependency.Events.SequenceEqual(["Root:Inject", "Child:Inject", "Root:EnterTree", "Child:EnterTree", "Child:Ready", "Root:Ready"]), "Scene injection must finish before any tree callback.");
		instance.Free();
		InjectionNode.Created.Clear();

		instance = factory.Instantiate<InjectionNode>(scene);
		Check(instance.GetParent() is null && !instance.IsInsideTree() && ReferenceEquals(instance.Dependency, dependency), "Creating a detached scene failed.");
		Check(instance.GetNode<InjectionNode>("Child").Dependency is null, "Detached scenes must also default to root-only injection.");
		instance.Free();
		InjectionNode.Created.Clear();

		child = new InjectionNode { Name = "Child", FailInjection = true };
		source = new InjectionNode();
		source.AddChild(child);
		child.Owner = source;
		using var failingChildScene = new PackedScene();
		Check(failingChildScene.Pack(source) == Error.Ok, "Packing the failing-child scene failed.");
		source.Free();
		InjectionNode.Created.Clear();
		instance = factory.Instantiate<InjectionNode>(failingChildScene, injection: SceneInjection.Root);
		Check(instance.GetNode<InjectionNode>("Child").Dependency is null, "Root mode must not invoke a descendant's failing injection method.");
		instance.Free();
		InjectionNode.Created.Clear();
		Throws<InjectionException>(() => factory.Instantiate<InjectionNode>(failingChildScene, injection: SceneInjection.Tree));
		Check(InjectionNode.Created.Count == 2 && InjectionNode.Created.All(node => !GodotObject.IsInstanceValid(node)), "Tree-mode injection failure must free the entire subtree.");
		InjectionNode.Created.Clear();
		Throws<ArgumentOutOfRangeException>(() => factory.Instantiate<InjectionNode>(scene, injection: (SceneInjection)42));
		Check(InjectionNode.Created.Count == 0, "An invalid injection mode must be rejected before instantiating a scene.");

		Throws<InvalidCastException>(() => factory.Instantiate<Node2D>(scene));
		Check(InjectionNode.Created.Count == 2 && InjectionNode.Created.All(node => !GodotObject.IsInstanceValid(node)), "A type mismatch must free the entire instantiated subtree.");
		InjectionNode.Created.Clear();

		using var empty = new ContainerBuilder().Build();
		Throws<InjectionException>(() => new SceneFactory(empty).Instantiate<InjectionNode>(scene, parent));
		Check(InjectionNode.Created.Count == 2 && InjectionNode.Created.All(node => !GodotObject.IsInstanceValid(node)), "A missing dependency must free the entire instantiated subtree.");
		InjectionNode.Created.Clear();

		source = new InjectionNode { FailInjection = true };
		using var failingScene = new PackedScene();
		Check(failingScene.Pack(source) == Error.Ok, "Packing the failing scene failed.");
		source.Free();
		InjectionNode.Created.Clear();
		Throws<InjectionException>(() => factory.Instantiate<InjectionNode>(failingScene, parent));
		Check(InjectionNode.Created.Count == 1 && !GodotObject.IsInstanceValid(InjectionNode.Created[0]), "A user exception must free the failed scene.");
		InjectionNode.Created.Clear();

		Throws<ArgumentNullException>(() => factory.Instantiate<Node>(scene: null!));
		using var unpacked = new PackedScene();
		Throws<ArgumentException>(() => factory.Instantiate<Node>(unpacked));
		var freedParent = new Node();
		freedParent.Free();
		Throws<ObjectDisposedException>(() => factory.Instantiate<Node>(scene, freedParent));
		Check(InjectionNode.Created.Count == 0, "An invalid parent must be rejected before instantiating a scene.");
		var builder = new ContainerBuilder();
		builder.RegisterInstance(dependency);
		var temporary = builder.Build();
		var detached = new SceneFactory(temporary).Instantiate<InjectionNode>(scene);
		temporary.Dispose();
		Check(GodotObject.IsInstanceValid(detached), "The factory must not transfer node ownership to the container.");
		detached.Free();
		InjectionNode.Created.Clear();

		using var root = new ContainerBuilder().Build();
		var childDependency = new InjectionDependency();
		using var childContainer = root.CreateChild(childBuilder => childBuilder.RegisterInstance(childDependency));
		instance = new SceneFactory(childContainer).Instantiate<InjectionNode>(scene, parent);
		Check(ReferenceEquals(instance.Dependency, childDependency), "Scene placement must not replace the factory's resolver.");
		instance.Free();
		InjectionNode.Created.Clear();
	}

	private static T Throws<T>(Action action) where T : Exception
	{
		try { action(); }
		catch (T error) { return error; }
		throw new InvalidOperationException($"Expected {typeof(T).Name}.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private class BaseTarget
	{
		public List<string> Calls { get; } = [];
		[Inject] public void BaseConstruct() => Calls.Add("Base");
	}
	private sealed class DerivedTarget : BaseTarget
	{
		public InjectionDependency? Dependency { get; private set; }
		[Inject] public void Construct(InjectionDependency dependency) { Dependency = dependency; Calls.Add("Derived"); }
	}
	private class VirtualTarget
	{
		public int Calls { get; protected set; }
		[Inject] public virtual void Construct() => Calls++;
	}
	private sealed class OverrideTarget : VirtualTarget
	{
		public override void Construct() => Calls++;
	}
	private sealed class MarkedOverrideTarget : VirtualTarget
	{
		[Inject] public override void Construct() => Calls++;
	}
	private sealed class DuplicateDerivedTarget : BaseTarget
	{
		[Inject] public void First() => Calls.Add("First");
		[Inject] public void Second() => Calls.Add("Second");
	}
	private sealed class OverrideAndAdditional : VirtualTarget
	{
		public override void Construct() => Calls++;
		[Inject] public void Additional() => Calls++;
	}
	private sealed class MarkedOverrideAndAdditional : VirtualTarget
	{
		[Inject] public override void Construct() => Calls++;
		[Inject] public void Additional() => Calls++;
	}

	private sealed class MultipleTarget
	{
		public int Calls { get; private set; }
		[Inject] public void First() => Calls++;
		[Inject] public void Second() => Calls++;
		public void Unmarked() => throw new InvalidOperationException();
	}
	private sealed class PrivateTarget { [Inject] private void Construct() { } }
	private sealed class PartiallyInvalidTarget
	{
		public int Calls { get; private set; }
		[Inject] public void Valid() => Calls++;
		[Inject] private void Invalid() { }
	}
	private sealed class StaticTarget { [Inject] public static void Construct() { } }
	private sealed class ReturnTarget { [Inject] public int Construct() => 0; }
	private sealed class GenericTarget { [Inject] public void Construct<T>() { } }
	private sealed class RefTarget { [Inject] public void Construct(ref int value) { } }
	private sealed class AsyncTarget { [Inject] public async void Construct() => await Task.Yield(); }
	private sealed class MissingTarget { [Inject] public void Construct(Unregistered dependency) { } }
	private sealed class ThrowingTarget { [Inject] public void Construct() => throw new InvalidOperationException("User error"); }
	private sealed class Unregistered;
	private sealed record ResourceHolder(InjectionResource Resource);
}

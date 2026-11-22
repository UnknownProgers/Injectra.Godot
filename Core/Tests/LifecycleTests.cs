using System.Runtime.InteropServices;
using Godot;

namespace Injectra.Godot.Tests;

public partial class LifecycleTests : Node
{
	public override void _Ready() => Callable.From(Run).CallDeferred();

	private void Run()
	{
		try
		{
			Check(System.Environment.Version.Major == 11, "Godot must actually load .NET 11.");
			GD.Print($"Runtime: {RuntimeInformation.FrameworkDescription}; Godot: {Engine.GetVersionInfo()["string"]}");
			DetachedHierarchyIsConstructedBeforeCallbacks();
			PackedSceneCanBePreparedBeforeAddChild();
			ReadyDoesNotRepeatWithoutRequestReady();
			InjectionTests.Run(this);
			GD.Print("PASS: 3 Godot lifecycle tests, .NET 11, Injectra integration.");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}

	private void DetachedHierarchyIsConstructedBeforeCallbacks()
	{
		var events = new List<string>();
		var parent = new OrderProbe { Name = "Parent", Events = events };
		var child = new OrderProbe { Name = "Child", Events = events };
		parent.AddChild(child);
		Check(events.Count == 0, "Detached nodes must not receive tree callbacks.");

		var builder = new ContainerBuilder();
		builder.Register<Dependency>(Lifetime.Singleton);
		using var container = builder.Build();
		parent.Construct(container.Resolve<Dependency>());
		child.Construct(container.Resolve<Dependency>());
		Check(ReferenceEquals(parent.Dependency, child.Dependency), "Nodes must share the registered singleton.");
		AddChild(parent);
		RemoveChild(parent);
		Expect(events, "Parent:Construct", "Child:Construct", "Parent:EnterTree", "Child:EnterTree",
			"Child:Ready", "Parent:Ready", "Child:ExitTree", "Parent:ExitTree");
		parent.Free();
	}

	private void PackedSceneCanBePreparedBeforeAddChild()
	{
		var source = new OrderProbe { Name = "Scene" };
		var child = new OrderProbe { Name = "Child" };
		source.AddChild(child);
		child.Owner = source;
		using var scene = new PackedScene();
		Check(scene.Pack(source) == Error.Ok, "Scene packing failed.");
		source.Free();

		var instance = scene.Instantiate<OrderProbe>();
		var events = new List<string>();
		var dependency = new Dependency();
		instance.Events = events;
		var instanceChild = instance.GetNode<OrderProbe>("Child");
		instanceChild.Events = events;
		Check(!instance.IsInsideTree() && !instanceChild.IsInsideTree(), "Instantiate must leave the subtree detached.");
		instance.Construct(dependency);
		instanceChild.Construct(dependency);
		AddChild(instance);
		RemoveChild(instance);
		Expect(events, "Scene:Construct", "Child:Construct", "Scene:EnterTree", "Child:EnterTree",
			"Child:Ready", "Scene:Ready", "Child:ExitTree", "Scene:ExitTree");
		instance.Free();
	}

	private void ReadyDoesNotRepeatWithoutRequestReady()
	{
		var events = new List<string>();
		var node = new OrderProbe { Name = "Reentered", Events = events };
		node.Construct(new Dependency());
		AddChild(node);
		RemoveChild(node);
		AddChild(node);
		RemoveChild(node);
		node.RequestReady();
		AddChild(node);
		RemoveChild(node);
		Check(events.Count(value => value == "Reentered:EnterTree") == 3, "EnterTree must run on every entry.");
		Check(events.Count(value => value == "Reentered:Ready") == 2, "Ready must repeat only after RequestReady.");
		node.Free();
	}

	private static void Expect(List<string> actual, params string[] expected) =>
		Check(actual.SequenceEqual(expected), $"Unexpected callback order: {string.Join(", ", actual)}");

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	public sealed class Dependency;
}

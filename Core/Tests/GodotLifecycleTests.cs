using Godot;
using Injectra.Lifecycle;

namespace Injectra.Godot.Tests;

internal static class GodotLifecycleTests
{
	public static async Task Run(Node parent)
	{
		var root = new LifecycleScopeProbe();
		parent.AddChild(root);
		Check(root.State.Initializations == 1 && root.State.Injected && !root.State.ReadyAtInitialization,
			"Root initialization must follow injection and must not require Ready.");
		var manager = root.Container.Resolve<InjectraLifecycle>();
		var source = new LifecycleScopeProbe();
		using var scene = new PackedScene();
		Check(scene.Pack(source) == Error.Ok, "Packing a lifecycle scope scene failed.");
		source.Free();
		var factoryScope = root.Container.Resolve<SceneFactory>().Instantiate<LifecycleScopeProbe>(scene, root);
		Check(factoryScope.State.Initializations == 1 && !factoryScope.State.InTreeAtInitialization,
			"Factory scope initialization must follow injection before AddChild.");
		factoryScope.Free();
		var child = new LifecycleScopeProbe();
		root.AddChild(child);
		Check(child.State.Initializations == 1 && !child.State.ReadyAtInitialization, "Child preparation must inject before initialization.");
		Check(ReferenceEquals(manager, child.Container.Resolve<InjectraLifecycle>()), "The hierarchy must share one manager.");

		root._Process(0.125);
		root._PhysicsProcess(0.025);
		Check(root.State.Frames == 1 && child.State.Frames == 1, "Frame dispatch must not repeat inherited participants.");
		Check(child.State.LastFrameDelta == 0.125 && child.State.LastPhysicsDelta == 0.025, "Godot deltas must reach the registered interfaces.");
		child._Process(0.5);
		child._PhysicsProcess(0.5);
		Check(child.State.Frames == 1 && child.State.PhysicsFrames == 1, "Child callbacks must not dispatch the manager again.");
		var closedState = child.State;
		child.Free();
		root._Process(0.1);
		Check(closedState.Frames == 1 && closedState.Disposals == 1, "Closed scopes must stop receiving updates and dispose owned services.");

		LifecycleScopeProbe? created = null;
		root.State.OnFrame = () =>
		{
			root.State.OnFrame = null;
			created = new LifecycleScopeProbe();
			root.AddChild(created);
			Check(created.State.Injected && created.State.Initializations == 0, "Creation during Tick must inject immediately and defer initialization.");
		};
		root._Process(0.1);
		Check(created is not null && created.State.Initializations == 1 && created.State.Frames == 0,
			"Deferred child initialization must finish after traversal and before the next frame.");
		child = created!;
		root._Process(0.1);
		Check(child.State.Frames == 1, "The new scope must receive the next frame.");

		var frameStart = root.State.Frames;
		var childFrameStart = child.State.Frames;
		var physicsStart = root.State.PhysicsFrames;
		await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
		await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
		await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.PhysicsFrame);
		await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(root.State.Frames > frameStart && root.State.PhysicsFrames > physicsStart, "Real Godot callbacks must drive both phases.");
		Check(root.State.Frames - frameStart == child.State.Frames - childFrameStart, "Real engine frames must dispatch each scope exactly once.");

		var frameOnlyInitialization = new LifecycleScopeProbe { ExposeFrame = false, ExposePhysics = false };
		root.AddChild(frameOnlyInitialization);
		root._Process(0.1);
		root._PhysicsProcess(0.02);
		Check(frameOnlyInitialization.State.Initializations == 1 && frameOnlyInitialization.State.Frames == 0
			&& frameOnlyInitialization.State.PhysicsFrames == 0, "Implemented but unregistered interfaces must not participate.");
		frameOnlyInitialization.Free();

		var failed = new LifecycleScopeProbe { FailInjection = true };
		try
		{
			failed.Prepare(parentContainer: root.Container);
			throw new InvalidOperationException("Expected injection failure.");
		}
		catch (InjectionException)
		{
		}
		Check(failed.State.Initializations == 0 && failed.State.Disposals == 1, "Failed scope injection must dispose without initialization.");
		failed.Free();
		failed = new LifecycleScopeProbe { FailInitialization = true };
		try
		{
			failed.Prepare(parentContainer: root.Container);
			throw new InvalidOperationException("Expected initialization failure.");
		}
		catch (InvalidOperationException error) when (error.Message == "Initialization failed.")
		{
		}
		Check(failed.State.Disposals == 1, "Failed child initialization must clean up its container.");
		failed.Free();
		root._Process(0.1);
		root.Free();
		Check(child.State.Disposals == 1, "Root shutdown must dispose child services.");
		GD.Print("PASS: Godot frame/physics lifecycle, prepare-before-initialize, child creation during Tick and shutdown.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}
}

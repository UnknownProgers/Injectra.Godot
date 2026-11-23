using Godot;
using Injectra.Lifecycle;

namespace Injectra.Godot.Demo;

public sealed class DemoSession : IDisposable, IInitializable, IFrameTickable, IPhysicsTickable
{
	public DemoSettings Settings { get; }

	public DemoSession(DemoSettings settings)
	{
		Settings = settings;
	}

	public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
	public bool IsDisposed { get; private set; }
	public Action? OnNextFrame { get; set; }
	public int Spawned { get; private set; }
	public bool IsInitialized { get; private set; }
	public int Frames { get; private set; }
	public int PhysicsFrames { get; private set; }
	public void Initialize() => IsInitialized = true;

	public void FrameTick(double deltaTime)
	{
		Frames++;
		var callback = OnNextFrame;
		OnNextFrame = null;
		callback?.Invoke();
	}

	public void PhysicsTick(double deltaTime) => PhysicsFrames++;
	public int NextActor() => ++Spawned;

	public void Dispose()
	{
		IsDisposed = true;
		OnNextFrame = null;
		GD.Print($"DemoSession {Id} disposed with its scope.");
	}
}

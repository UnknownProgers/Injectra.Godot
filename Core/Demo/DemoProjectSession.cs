using Godot;
using Injectra.Lifecycle;

namespace Injectra.Godot.Demo;

public sealed class DemoProjectSession : IDisposable, IInitializable, IFrameTickable, IPhysicsTickable
{
	public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
	public bool IsDisposed { get; private set; }
	public bool IsInitialized { get; private set; }
	public int Frames { get; private set; }
	public int PhysicsFrames { get; private set; }
	public void Initialize() => IsInitialized = true;
	public void FrameTick(double deltaTime) => Frames++;
	public void PhysicsTick(double deltaTime) => PhysicsFrames++;

	public void Dispose()
	{
		IsDisposed = true;
		GD.Print("DemoProjectSession disposed with the project scope.");
	}
}

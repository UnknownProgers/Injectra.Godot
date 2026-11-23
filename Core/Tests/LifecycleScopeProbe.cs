using Godot;
using Injectra.Lifecycle;

namespace Injectra.Godot.Tests;

public partial class LifecycleScopeProbe : InjectraScope, IInitializable, IFrameTickable, IPhysicsTickable
{
	public LifecycleProbeState State { get; } = new();
	public bool ExposeFrame { get; set; } = true;
	public bool ExposePhysics { get; set; } = true;
	public bool FailInjection { get; set; }
	public bool FailInitialization { get; set; }

	protected override void Configure(ContainerBuilder builder)
	{
		builder.RegisterInstance(State);
		var registration = builder.RegisterInstance(this).As<IInitializable>();
		if (ExposeFrame) registration.As<IFrameTickable>();
		if (ExposePhysics) registration.As<IPhysicsTickable>();
		builder.Register<OwnedProbe>(_ => new OwnedProbe(State), Lifetime.Singleton).NonLazy();
	}

	[Inject]
	public void Construct(LifecycleProbeState state)
	{
		state.Injected = true;
		if (FailInjection)
			throw new InvalidOperationException("Injection failed.");
	}

	public void Initialize()
	{
		if (!State.Injected)
			throw new InvalidOperationException("Initialize ran before injection.");
		if (FailInitialization)
			throw new InvalidOperationException("Initialization failed.");
		State.Initializations++;
		State.ReadyAtInitialization = State.Ready;
		State.InTreeAtInitialization = IsInsideTree();
	}

	public void FrameTick(double deltaTime)
	{
		if (State.Initializations != 1)
			throw new InvalidOperationException("Tick ran without initialization.");
		State.Frames++;
		State.LastFrameDelta = deltaTime;
		State.OnFrame?.Invoke();
	}

	public void PhysicsTick(double deltaTime)
	{
		State.PhysicsFrames++;
		State.LastPhysicsDelta = deltaTime;
	}

	public override void _Ready() => State.Ready = true;

	private sealed class OwnedProbe(LifecycleProbeState state) : IDisposable
	{
		public void Dispose() => state.Disposals++;
	}
}
public sealed class LifecycleProbeState
{
	public bool Injected { get; set; }
	public bool Ready { get; set; }
	public bool ReadyAtInitialization { get; set; }
	public bool InTreeAtInitialization { get; set; }
	public int Initializations { get; set; }
	public int Frames { get; set; }
	public int PhysicsFrames { get; set; }
	public double LastFrameDelta { get; set; }
	public double LastPhysicsDelta { get; set; }
	public int Disposals { get; set; }
	public Action? OnFrame { get; set; }
}

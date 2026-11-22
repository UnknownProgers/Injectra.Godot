using Godot;

namespace Injectra.Godot.Demo;

public sealed class DemoSession : IDisposable
{
	public int Spawned { get; private set; }
	public int NextActor() => ++Spawned;
	public void Dispose() => GD.Print("DemoSession disposed with its scope.");
}

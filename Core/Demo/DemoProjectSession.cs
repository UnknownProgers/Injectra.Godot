using Godot;

namespace Injectra.Godot.Demo;

public sealed class DemoProjectSession : IDisposable
{
	public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
	public bool IsDisposed { get; private set; }
	public void Dispose()
	{
		IsDisposed = true;
		GD.Print("DemoProjectSession disposed with the project scope.");
	}
}

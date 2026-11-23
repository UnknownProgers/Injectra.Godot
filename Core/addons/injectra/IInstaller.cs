#nullable enable

namespace Injectra.Godot;

/// <summary>Configures service registrations before a scope builds its container.</summary>
public interface IInstaller
{
	/// <summary>Adds registrations to the supplied builder without building it or starting game logic.</summary>
	void Install(ContainerBuilder builder);
}

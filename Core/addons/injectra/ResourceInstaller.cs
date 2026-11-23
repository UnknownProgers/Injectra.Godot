#nullable enable

using Godot;

namespace Injectra.Godot;

/// <summary>Base class for installers assigned to a scope in the Godot inspector.</summary>
[GlobalClass]
public abstract partial class ResourceInstaller : Resource, IInstaller
{
	/// <summary>Adds registrations to the scope's builder before the container is built.</summary>
	public abstract void Install(ContainerBuilder builder);
}

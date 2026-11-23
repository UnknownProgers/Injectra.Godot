#nullable enable

namespace Injectra.Godot;

/// <summary>Determines which nodes receive method injection when a scene is instantiated.</summary>
public enum SceneInjection
{
	/// <summary>Injects only the scene root, without traversing its descendants.</summary>
	Root,

	/// <summary>Injects the scene root and its descendants in parent-first order.</summary>
	Tree
}

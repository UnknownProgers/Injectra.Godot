using Godot;
using Godot.Collections;

namespace Injectra.Godot;

/// <summary>Inspector-authored scene mappings, snapshotted when a scope is prepared.</summary>
[GlobalClass]
[Icon("res://addons/Injectra.Godot/icons/scene-registry.svg")]
public partial class InjectraSceneRegistry : Resource
{
	/// <summary>Local mappings. Duplicate script keys are rejected during preparation.</summary>
	[Export] public Array<InjectraSceneRegistration> Entries { get; set; } = [];

	internal System.Collections.Generic.Dictionary<string, PackedScene> CreateLookup()
	{
		var scenes = new System.Collections.Generic.Dictionary<string, PackedScene>(StringComparer.Ordinal);
		foreach (var entry in Entries)
		{
			if (entry is null || !IsInstanceValid(entry)
			    || entry.Script is null || !IsInstanceValid(entry.Script)
			    || string.IsNullOrEmpty(entry.Script.ResourcePath)
			    || entry.Scene is null || !IsInstanceValid(entry.Scene) || !entry.Scene.CanInstantiate())
				throw new InvalidOperationException("A scene registration requires a saved C# script and an instantiable packed scene.");

			if (!scenes.TryAdd(entry.Script.ResourcePath, entry.Scene))
				throw new InvalidOperationException($"Duplicate scene registration for {entry.Script.ResourcePath}.");
		}
		return scenes;
	}
}

using Godot;

namespace Injectra.Godot;

/// <summary>Associates a C# node script with a packed scene.</summary>
[GlobalClass]
public partial class SceneRegistration : Resource
{
	/// <summary>The exact C# node type used when requesting the scene.</summary>
	[Export] public CSharpScript Script { get; set; } = null!;

	/// <summary>The scene whose root implements the selected script type.</summary>
	[Export] public PackedScene Scene { get; set; } = null!;
}

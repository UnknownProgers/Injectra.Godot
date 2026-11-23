using System;
using Godot;

namespace Injectra.Godot;

/// <summary>Provides the application container. Add its scene as an Autoload before other dependent Autoloads.</summary>
/// <remarks>Only one project scope can be active. Ordinary scopes without an explicit parent inherit its container.</remarks>
[GlobalClass]
[Icon("res://addons/Injectra.Godot/icons/scope.svg")]
public partial class InjectraProjectScope : InjectraScope
{
	internal static InjectraProjectScope? Active { get; private set; }

	internal void Activate()
	{
		if (Active is not null && !ReferenceEquals(Active, this))
			throw new InvalidOperationException("Only one project scope can be active.");

		Active = this;
	}

	internal void Deactivate()
	{
		if (ReferenceEquals(Active, this))
			Active = null;
	}
}

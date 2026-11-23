using System;
using System.Collections.Generic;
using Godot;

namespace Injectra.Godot.Tests;

public partial class InjectionNode : Node
{
	public static List<InjectionNode> Created { get; } = [];
	public InjectionDependency? Dependency { get; private set; }
	public bool ReadyWithDependency { get; private set; }
	[Export] public bool FailInjection { get; set; }

	public InjectionNode() => Created.Add(this);

	[Inject]
	public void Construct(InjectionDependency dependency)
	{
		Dependency = dependency;
		dependency.Events.Add($"{Name}:Inject");
		if (FailInjection)
			throw new InvalidOperationException("Deliberate injection failure.");
	}

	public override void _EnterTree() => Dependency?.Events.Add($"{Name}:EnterTree");

	public override void _Ready()
	{
		ReadyWithDependency = Dependency is not null;
		Dependency?.Events.Add($"{Name}:Ready");
	}
}

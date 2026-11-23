using Godot;
using Injectra.Godot.Generation;

namespace Injectra.Godot.Tests;

internal static partial class ReflectionFallbackTests
{
	internal static void Run()
	{
		var dependency = new InjectionDependency();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(dependency);
		using var container = builder.Build();
		var node = new Leaf();
		try
		{
			if ((object)node is IGeneratedInjectable)
				throw new InvalidOperationException("Nested test types must exercise reflection fallback.");
			var injector = new MethodInjector(container);
			injector.Inject(node);
			injector.Inject(node);
			if (!ReferenceEquals(node.Dependency, dependency)
			    || !node.Calls.SequenceEqual(["Override", "Leaf", "Override", "Leaf"]))
				throw new InvalidOperationException("Fallback must preserve user ancestry, overrides, order and cached repeated injection above Node2D.");
		}
		finally
		{
			node.Free();
		}
		GD.Print("PASS: reflection fallback preserves user inheritance above Godot framework classes.");
	}

	private partial class Base : Node2D
	{
		internal List<string> Calls { get; } = [];
		internal InjectionDependency? Dependency { get; set; }

		[Inject]
		public virtual void Construct(InjectionDependency dependency) => throw new InvalidOperationException("The override must replace this slot.");
	}

	private partial class Middle : Base
	{
		public override void Construct(InjectionDependency dependency)
		{
			Dependency = dependency;
			Calls.Add("Override");
		}
	}

	private partial class Leaf : Middle
	{
		[Inject]
		public void Additional() => Calls.Add("Leaf");
	}
}

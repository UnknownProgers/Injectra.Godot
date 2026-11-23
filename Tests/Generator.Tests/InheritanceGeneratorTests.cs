using Microsoft.CodeAnalysis;
using Xunit;

namespace Injectra.Godot.Generation.Tests;

public sealed partial class MethodInjectionGeneratorTests
{
	[Fact]
	public void GeneratesBaseToDerivedPlanForALeafWithoutLocalAttributes()
	{
		var result = Generate("""
			using Injectra;
			public abstract partial class Base : Godot.Node
			{
				public System.Collections.Generic.List<string> Calls = new();
				[Inject] public void BaseConstruct() => Calls.Add("Base");
			}
			public partial class Middle : Base { [Inject] public void MiddleConstruct() => Calls.Add("Middle"); }
			public partial class Leaf : Middle { }
			public static class Entry
			{
				public static bool Run()
				{
					var node = new Leaf();
					using var container = new ContainerBuilder().Build();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container)
						&& string.Join(",", node.Calls) == "Base,Middle";
				}
			}
			""");
		Assert.Equal(2, result.Generated.Length);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("")]
	[InlineData("[Inject]")]
	public void OverrideRunsOnceWithOrWithoutRepeatedAttribute(string attribute)
	{
		var result = Generate($$"""
			using Injectra;
			public partial class Base : Godot.Node
			{
				public int Calls;
				[Inject] public virtual void Construct() => throw new System.Exception("Base body must not run");
			}
			public partial class Child : Base { {{attribute}} public override void Construct() => Calls++; }
			public partial class Leaf : Child { }
			public static class Entry
			{
				public static bool Run()
				{
					var node = new Leaf();
					using var container = new ContainerBuilder().Build();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && node.Calls == 1;
				}
			}
			""");
		Assert.Equal(3, result.Generated.Length);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void HiddenMethodsAreBothCalledThroughTheirDeclaringTypes()
	{
		var result = Generate("""
			using Injectra;
			public sealed class Service;
			public partial class Base : Godot.Node
			{
				public string Calls = "";
				[Inject] public void Construct(Service value) => Calls += "Base;";
			}
			public partial class Child : Base { [Inject] public new void Construct(Service value) => Calls += "Child;"; }
			public static class Entry
			{
				public static bool Run()
				{
					var builder = new ContainerBuilder();
					builder.RegisterInstance(new Service());
					using var container = builder.Build();
					var node = new Child();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && node.Calls == "Base;Child;";
				}
			}
			""");
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void NewVirtualMethodStartsAnIndependentSlot()
	{
		var result = Generate("""
			using Injectra;
			public partial class Base : Godot.Node
			{
				public string Calls = "";
				[Inject] public virtual void Construct() => Calls += "Base;";
			}
			public partial class Middle : Base { [Inject] public new virtual void Construct() => Calls += "Wrong;"; }
			public partial class Leaf : Middle { [Inject] public override void Construct() => Calls += "Leaf;"; }
			public static class Entry
			{
				public static bool Run()
				{
					var node = new Leaf();
					using var container = new ContainerBuilder().Build();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && node.Calls == "Base;Leaf;";
				}
			}
			""");
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void OverrideRetainsItsOriginalBaseSlotPosition()
	{
		var result = Generate("""
			using Injectra;
			public partial class Base : Godot.Node
			{
				public string Calls = "";
				[Inject] public virtual void Construct() => Calls += "Wrong;";
			}
			public partial class Middle : Base { [Inject] public void MiddleConstruct() => Calls += "Middle;"; }
			public partial class Leaf : Middle { public override void Construct() => Calls += "Override;"; }
			public static class Entry
			{
				public static bool Run()
				{
					var node = new Leaf();
					using var container = new ContainerBuilder().Build();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && node.Calls == "Override;Middle;";
				}
			}
			""");
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void AbstractInjectionMethodIsImplementedByTheConcreteOverride()
	{
		var result = Generate("""
			using Injectra;
			public abstract partial class Base : Godot.Node
			{
				public int Calls;
				[Inject] public abstract void Construct();
			}
			public partial class Child : Base { public override void Construct() => Calls++; }
			public static class Entry
			{
				public static bool Run()
				{
					var node = new Child();
					using var container = new ContainerBuilder().Build();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && node.Calls == 1;
				}
			}
			""");
		Assert.Single(result.Generated);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ClosedGenericBaseParametersAreSubstituted()
	{
		var result = Generate("""
			using Injectra;
			public sealed class Service;
			public abstract partial class Base<T> : Godot.Node
			{
				public object? Received;
				[Inject] public void Construct(T dependency) => Received = dependency;
			}
			public partial class Child : Base<Service> { }
			public static class Entry
			{
				public static bool Run()
				{
					var value = new Service();
					var builder = new ContainerBuilder();
					builder.RegisterInstance(value);
					using var container = builder.Build();
					var node = new Child();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)node).TryInject(node.GetType(), container) && ReferenceEquals(value, node.Received);
				}
			}
			""");
		Assert.Contains("resolver.Resolve<global::Service>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("")]
	[InlineData("[Inject]")]
	public void OverrideAndAnotherInjectionMethodAreDuplicates(string attribute)
	{
		var result = GenerateWithDiagnostics($$"""
			using Injectra;
			public partial class Base : Godot.Node { [Inject] public virtual void Construct() { } }
			public partial class Child : Base
			{
				{{attribute}} public override void Construct() { }
				[Inject] public void Additional() { }
			}
			""");
		var error = Assert.Single(result.Diagnostics);
		Assert.Equal("IGG001", error.Id);
		Assert.Equal(DiagnosticSeverity.Error, error.Severity);
		Assert.Contains("Child", error.GetMessage());
	}

	[Fact]
	public void DuplicateMethodsAcrossPartialDeclarationsProduceOneDiagnostic()
	{
		var result = GenerateWithDiagnostics(
			"public partial class Target : Godot.Node { [Injectra.Inject] public void First() { } }",
			"public partial class Target { [Injectra.Inject] public void Second() { } }");
		Assert.Equal("IGG001", Assert.Single(result.Diagnostics).Id);
		Assert.Empty(result.Generated);
	}

	[Fact]
	public void InvalidBaseMethodKeepsTheWholePlanOnReflection()
	{
		var result = Generate("""
			using Injectra;
			public partial class Base : Godot.Node
			{
				[Inject] public virtual async void Construct() => await System.Threading.Tasks.Task.Yield();
			}
			public partial class Child : Base { public override void Construct() { } }
			""");
		Assert.Empty(result.Generated);
	}

	[Fact]
	public void APartialMethodIsNotCountedTwice()
	{
		var result = Generate("""
			using Injectra;
			public partial class Target : Godot.Node
			{
				[Inject] public partial void Construct();
				public partial void Construct() { }
			}
			""");
		Assert.Empty(result.Generated);
	}
}

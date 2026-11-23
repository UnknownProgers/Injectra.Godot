using Xunit;

namespace Injectra.Godot.Generation.Tests;

public sealed partial class MethodInjectionGeneratorTests
{
	[Fact]
	public void EmptyPlanAcceptsOnlyItsExactTypeWithoutUsingResolver()
	{
		var result = Generate("""
			public partial class Empty : Godot.Node;
			public class UngeneratedChild : Empty
			{
				[Injectra.Inject] public void Construct() => throw new System.Exception("Must use fallback");
			}
			public static class Entry
			{
				public static bool Run()
				{
					var empty = (Injectra.Godot.Generation.IGeneratedInjectable)new Empty();
					var child = new UngeneratedChild();
					return empty.TryInject(typeof(Empty), null!)
						&& !empty.TryInject(typeof(UngeneratedChild), null!)
						&& !((Injectra.Godot.Generation.IGeneratedInjectable)child).TryInject(child.GetType(), null!);
				}
			}
			""");
		Assert.Single(result.Generated);
		Assert.DoesNotContain("resolver.Resolve", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void GeneratedChildOfEmptyBaseExecutesItsOwnInjection()
	{
		var result = Generate("""
			public partial class Empty : Godot.Node;
			public partial class Child : Empty
			{
				public int Calls;
				[Injectra.Inject] public void Construct() => Calls++;
			}
			public static class Entry
			{
				public static bool Run()
				{
					var child = new Child();
					return ((Injectra.Godot.Generation.IGeneratedInjectable)child).TryInject(child.GetType(), null!) && child.Calls == 1;
				}
			}
			""");
		Assert.Equal(2, result.Generated.Length);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("public class Empty : Godot.Node;")]
	[InlineData("public partial class Empty<T> : Godot.Node;")]
	[InlineData("public partial class Holder { public partial class Empty : Godot.Node; }")]
	public void UnsupportedEmptyTargetsStillUseFallback(string declaration)
	{
		Assert.Empty(Generate(declaration).Generated);
	}
}

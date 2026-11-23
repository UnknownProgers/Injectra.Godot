using Microsoft.CodeAnalysis;
using Xunit;

namespace Injectra.Godot.Generation.Tests;

public sealed partial class MethodInjectionGeneratorTests
{
	[Theory]
	[InlineData("[Inject] private void Construct() { }", "public", "Construct")]
	[InlineData("[Inject] protected void Construct() { }", "public", "Construct")]
	[InlineData("[Inject] internal void Construct() { }", "public", "Construct")]
	[InlineData("[Inject] public static void Construct() { }", "instance", "Construct")]
	[InlineData("[Inject] public int Construct() => 0;", "void", "Construct")]
	[InlineData("[Inject] public System.Threading.Tasks.Task Construct() => System.Threading.Tasks.Task.CompletedTask;", "void", "Construct")]
	[InlineData("[Inject] public async void Construct() { await System.Threading.Tasks.Task.Yield(); }", "synchronous", "Construct")]
	[InlineData("[Inject, System.Runtime.CompilerServices.AsyncStateMachine(typeof(object))] public void Construct() { }", "synchronous", "Construct")]
	[InlineData("[Inject] public void Construct<T>() { }", "generic", "Construct")]
	[InlineData("[Inject] public void Construct(ref int value) { }", "ref, out or in", "value")]
	[InlineData("[Inject] public void Construct(out int value) { value = 0; }", "ref, out or in", "value")]
	[InlineData("[Inject] public void Construct(in int value) { }", "ref, out or in", "value")]
	[InlineData("[Inject] public void Construct(System.Span<int> value) { }", "ref-struct", "value")]
	[InlineData("[Inject] public unsafe void Construct(int* value) { }", "pointer", "value")]
	[InlineData("[Inject] public unsafe void Construct(delegate*<void> callback) { }", "function pointer", "callback")]
	[InlineData("[Inject] public void Construct(__arglist) { }", "variable argument", "Construct")]
	public void InvalidSignatureProducesAnErrorAtTheOffendingMember(string method, string reason, string token)
	{
		var result = GenerateWithDiagnostics($$"""
			using Injectra;
			public partial class Target : Godot.Node { {{method}} }
			""");

		var diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal("IGG002", diagnostic.Id);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Contains("Target.Construct", diagnostic.GetMessage());
		Assert.Contains(reason, diagnostic.GetMessage());
		Assert.Equal(token, diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
		Assert.Empty(result.Generated);
	}

	[Theory]
	[InlineData("public abstract partial class Target : Godot.Node")]
	[InlineData("public partial class Target<T> : Godot.Node")]
	[InlineData("public class Target : Godot.Node")]
	public void InvalidSignaturesAreReportedEvenWhenTheTargetCannotBeGenerated(string declaration)
	{
		var result = GenerateWithDiagnostics($$"""
			{{declaration}} { [Injectra.Inject] private void Construct() { } }
			""");
		Assert.Equal("IGG002", Assert.Single(result.Diagnostics).Id);
		Assert.Empty(result.Generated);
	}

	[Fact]
	public void InvalidNestedNodeMethodIsReported()
	{
		var result = GenerateWithDiagnostics("""
			public partial class Holder
			{
				public partial class Target : Godot.Node { [Injectra.Inject] private void Construct() { } }
			}
			""");
		Assert.Equal("IGG002", Assert.Single(result.Diagnostics).Id);
	}

	[Theory]
	[InlineData("")]
	[InlineData("[Inject]")]
	public void InvalidOverrideIsReportedEvenWithoutARepeatedAttribute(string attribute)
	{
		var result = GenerateWithDiagnostics($$"""
			using Injectra;
			public partial class Base : Godot.Node { [Inject] public virtual void Construct() { } }
			public partial class Child : Base
			{
				{{attribute}} public override async void Construct() => await System.Threading.Tasks.Task.Yield();
			}
			""");
		var diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal("IGG002", diagnostic.Id);
		Assert.Contains("Child.Construct", diagnostic.GetMessage());
		Assert.Single(result.Generated); // Only the valid base gets a generated plan.
	}

	[Fact]
	public void PartialImplementationIsCheckedForAsyncVoid()
	{
		var result = GenerateWithDiagnostics("""
			using Injectra;
			public partial class Target : Godot.Node
			{
				[Inject] public partial void Construct();
				public async partial void Construct() => await System.Threading.Tasks.Task.Yield();
			}
			""");
		var diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal("IGG002", diagnostic.Id);
		Assert.Contains("synchronous", diagnostic.GetMessage());
	}

	[Fact]
	public void ASignatureDeclaredInAnotherPartialFileIsReportedOnce()
	{
		var result = GenerateWithDiagnostics(
			"public partial class Target : Godot.Node { }",
			"public partial class Target { [Injectra.Inject] private void Construct() { } }");
		var diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal("IGG002", diagnostic.Id);
		Assert.Contains("private void Construct", diagnostic.Location.SourceTree!.ToString());
	}

	[Fact]
	public void PlainServicesAreOutsideTheGodotGeneratorScope()
	{
		var result = Generate("public class Service { [Injectra.Inject] private void Construct() { } }");
		Assert.Empty(result.Generated);
	}

	[Fact]
	public void UnmarkedMethodsAreNotValidatedAsInjectionMethods()
	{
		var result = Generate("""
			public partial class Target : Godot.Node
			{
				[Injectra.Inject] public void Construct() { }
				private static int Helper() => 1;
			}
			""");
		Assert.Single(result.Generated);
	}
}

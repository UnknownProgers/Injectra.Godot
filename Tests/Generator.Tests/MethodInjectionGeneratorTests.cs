using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Injectra.Godot.Generation.Tests;

public sealed partial class MethodInjectionGeneratorTests
{
	private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator).Append(typeof(IResolver).Assembly.Location).Distinct(StringComparer.Ordinal)
		.Select(path => MetadataReference.CreateFromFile(path)).ToArray();

	[Fact]
	public void GeneratesDirectTypedCallsAndExecutesThem()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service;
		                      public partial class Target : Godot.Node
		                      {
		                      	public Service? Dependency { get; private set; }
		                      	[Inject] public void Construct(Service dependency) => Dependency = dependency;
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var service = new Service();
		                      		var builder = new ContainerBuilder();
		                      		builder.RegisterInstance(service);
		                      		using var container = builder.Build();
		                      		var target = new Target();
		                      		var generated = (Injectra.Godot.Generation.IGeneratedInjectable)target;
		                      		return generated.TryInject(target.GetType(), container) && ReferenceEquals(service, target.Dependency);
		                      	}
		                      }
		                      """);

		Assert.Contains("resolver.Resolve<global::Service>()", result.Source);
		Assert.Contains("((global::Target)this).@Construct(argument0)", result.Source);
		Assert.DoesNotContain("MethodInfo", result.Source);
		Assert.DoesNotContain("new object[", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void DynamicParameterUsesTheDeclaredObjectOverload()
	{
		var result = Generate("""
		                      using Injectra;
		                      public partial class Target : Godot.Node
		                      {
		                      	public bool Correct;
		                      	[Inject] public void Construct(dynamic value) => Correct = true;
		                      	public void Construct(string value) => throw new System.Exception("Wrong overload");
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.RegisterInstance<object>("text");
		                      		using var container = builder.Build();
		                      		var target = new Target();
		                      		return ((Injectra.Godot.Generation.IGeneratedInjectable)target).TryInject(target.GetType(), container) && target.Correct;
		                      	}
		                      }
		                      """);
		Assert.Contains("resolver.Resolve<global::System.Object>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ExceptionsPreserveResolutionAndUserCauses()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service;
		                      public partial class Target : Godot.Node
		                      {
		                      	[Inject] public void Construct(Service dependency) => throw new System.InvalidOperationException("User failure");
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var target = (Injectra.Godot.Generation.IGeneratedInjectable)new Target();
		                      		using var empty = new ContainerBuilder().Build();
		                      		try { target.TryInject(typeof(Target), empty); return false; }
		                      		catch (Injectra.Godot.InjectionException error)
		                      		{
		                      			if (error.InnerException is not ResolutionException || !error.Message.Contains("Construct")) return false;
		                      		}
		                      		var builder = new ContainerBuilder();
		                      		builder.RegisterInstance(new Service());
		                      		using var container = builder.Build();
		                      		try { target.TryInject(typeof(Target), container); return false; }
		                      		catch (Injectra.Godot.InjectionException error)
		                      		{
		                      			return error.InnerException is System.InvalidOperationException { Message: "User failure" };
		                      		}
		                      	}
		                      }
		                      """);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void InheritedGeneratedImplementationRefusesAnUncoveredDerivedType()
	{
		var result = Generate("""
		                      using Injectra;
		                      public partial class Parent : Godot.Node
		                      {
		                      	public int Calls;
		                      	[Inject] public void Construct() => Calls++;
		                      }
		                      public class Child : Parent { [Inject] public void Additional() { } }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var child = new Child();
		                      		using var container = new ContainerBuilder().Build();
		                      		return !((Injectra.Godot.Generation.IGeneratedInjectable)child).TryInject(child.GetType(), container) && child.Calls == 0;
		                      	}
		                      }
		                      """);
		Assert.Single(result.Generated);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("[Inject, System.Diagnostics.Conditional(\"UNDEFINED\")] public void Construct() { }")]
	[InlineData("[Inject, System.Obsolete(\"forbidden\", true)] public void Construct() { }")]
	[InlineData("[Inject, System.Obsolete(\"legacy\", DiagnosticId = \"CUSTOM001\")] public void Construct() { }")]
	[InlineData("[Inject, System.Diagnostics.CodeAnalysis.Experimental(\"CUSTOM002\")] public void Construct() { }")]
	public void UnsupportedMethodsStayOnReflection(string method)
	{
		var result = Generate($$"""
		                        using Injectra;
		                        public partial class Target : Godot.Node { {{method}} }
		                        """);
		Assert.Empty(result.Generated);
	}

	[Theory]
	[InlineData("public partial class Target<T> : Godot.Node { [Inject] public void Construct(T value) { } }")]
	[InlineData("public class Target : Godot.Node { [Inject] public void Construct() { } }")]
	[InlineData("public partial class Target { [Inject] public void Construct() { } }")]
	[InlineData("public partial class Holder { public partial class Target : Godot.Node { [Inject] public void Construct() { } } }")]
	public void CorrectUnsupportedTargetsKeepReflectionFallback(string declaration)
	{
		Assert.Empty(Generate("using Injectra;\n" + declaration).Generated);
	}

	[Fact]
	public void PartialDeclarationsProduceOneStableSource()
	{
		const string first = "public partial class Target : Godot.Node { [Injectra.Inject] public void Construct() { } }";
		const string second = "public partial class Target { [System.Obsolete] public void Other() { } }";
		var result = Generate(first, second);
		Assert.Single(result.Generated);
		Assert.Equal(result.Source, Generate(first, second).Source);
		Assert.DoesNotContain("\r", result.Source);
	}

	[Fact]
	public void UnrelatedAttributeDoesNotTriggerInjection()
	{
		var result = Generate("""
		                      public sealed class InjectAttribute : System.Attribute;
		                      public partial class Target : Godot.Node { [Inject] public void Construct() { } }
		                      """);
		Assert.Single(result.Generated);
		Assert.DoesNotContain(".@Construct(", result.Source);
	}

	private static (CSharpCompilation Compilation, string Source, SyntaxTree[] Generated) Generate(params string[] sources)
	{
		var result = GenerateWithDiagnostics(sources);
		Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
		return (result.Compilation, result.Source, result.Generated);
	}

	private static (CSharpCompilation Compilation, string Source, SyntaxTree[] Generated, System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics) GenerateWithDiagnostics(params string[] sources)
	{
		var parse = new CSharpParseOptions(LanguageVersion.Preview);
		var support = new[]
		{
			"namespace Godot { public class Node { } }",
			File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuntimeSources", "IGeneratedInjectable.cs")),
			File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuntimeSources", "InjectionException.cs")),
			"global using System;"
		};
		var compilation = CSharpCompilation.Create("GeneratedGame_" + Guid.NewGuid().ToString("N"),
			sources.Concat(support).Select(source => CSharpSyntaxTree.ParseText(source, parse)), References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new MethodInjectionGenerator().AsSourceGenerator()], parseOptions: parse);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
		var errors = updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
		Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
		var generated = updated.SyntaxTrees.Skip(compilation.SyntaxTrees.Count()).ToArray();
		return ((CSharpCompilation)updated, string.Join("\n", generated.Select(tree => tree.ToString())), generated, diagnostics);
	}

	private static object? Run(CSharpCompilation compilation)
	{
		using var output = new MemoryStream();
		var result = compilation.Emit(output);
		Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
		return Assembly.Load(output.ToArray()).GetType("Entry")!.GetMethod("Run")!.Invoke(null, null);
	}
}

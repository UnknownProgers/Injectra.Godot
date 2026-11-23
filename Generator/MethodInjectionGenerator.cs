using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Injectra.Godot.Generation;

/// <summary>Generates direct injection calls for simple partial Node classes.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class MethodInjectionGenerator : IIncrementalGenerator
{
	private const string InjectAttribute = "Injectra.InjectAttribute";
	private const string GeneratedContract = "Injectra.Godot.Generation.IGeneratedInjectable";

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var sources = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is ClassDeclarationSyntax declaration
				&& declaration.Members.OfType<MethodDeclarationSyntax>().Any(method => method.AttributeLists.Count != 0),
			static (syntax, _) => Describe(syntax));

		context.RegisterSourceOutput(sources.Where(source => source.HasValue), static (output, source) =>
		{
			var generated = source!.Value;
			output.AddSource(generated.Hint, SourceText.From(generated.Source, new UTF8Encoding(false)));
		});
	}

	private static (string Hint, string Source)? Describe(GeneratorSyntaxContext syntax)
	{
		var declaration = (ClassDeclarationSyntax)syntax.Node;
		if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
			|| syntax.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type
			|| type.ContainingType is not null || type.IsGenericType || type.IsAbstract || type.IsStatic || type.IsFileLocal)
			return null;

		var compilation = syntax.SemanticModel.Compilation;
		var attribute = compilation.GetTypeByMetadataName(InjectAttribute);
		var nodeType = compilation.GetTypeByMetadataName("Godot.Node");
		if (attribute is null || nodeType is null || compilation.GetTypeByMetadataName(GeneratedContract) is null)
			return null;

		var isNode = false;
		for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(parent, nodeType))
				isNode = true;
			// Inheritance plans and custom external bases stay on reflection in this first implementation.
			if (parent.GetMembers().OfType<IMethodSymbol>().Any(method => HasAttribute(method, attribute))
				|| (!SymbolEqualityComparer.Default.Equals(parent.ContainingAssembly, compilation.Assembly)
					&& parent.ContainingAssembly.Name is not ("GodotSharp" or "System.Private.CoreLib" or "System.Runtime")))
				return null;
		}
		if (!isNode)
			return null;

		var methods = type.GetMembers().OfType<IMethodSymbol>().Where(method => HasAttribute(method, attribute)).ToArray();
		if (methods.Length != 1 || !Supported(methods[0]))
			return null;
		var injection = methods[0];
		// Emit once even when the class is split across several source files.
		if (!injection.DeclaringSyntaxReferences.Any(reference => reference.SyntaxTree == declaration.SyntaxTree && declaration.Span.Contains(reference.Span)))
			return null;

		return (type.ToDisplayString() + ".Injectra.g.cs", Emit(type, injection));
	}

	private static bool Supported(IMethodSymbol method) => method.MethodKind == MethodKind.Ordinary
		&& method.DeclaredAccessibility == Accessibility.Public && !method.IsStatic && method.ReturnsVoid
		&& !method.IsGenericMethod && !method.IsAsync && !method.IsVararg && !method.IsPartialDefinition
		&& method.PartialDefinitionPart is null
		&& method.Parameters.All(parameter => parameter.RefKind == RefKind.None
			&& !parameter.Type.IsRefLikeType && parameter.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer))
		&& !method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
			"System.Diagnostics.ConditionalAttribute" or "System.Runtime.CompilerServices.AsyncStateMachineAttribute")
		&& !PreventsDirectCall(method) && !PreventsDirectCall(method.ContainingType);

	private static bool HasAttribute(IMethodSymbol method, INamedTypeSymbol attribute) => method.GetAttributes()
		.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));

	private static bool PreventsDirectCall(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
		attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute"
		|| (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute"
			&& ((attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value is true)
				|| attribute.NamedArguments.Any(argument => argument.Key == "DiagnosticId" && argument.Value.Value is string id && id.Length != 0))));

	private static string Emit(INamedTypeSymbol type, IMethodSymbol method)
	{
		var target = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var body = new StringBuilder();
		for (var i = 0; i < method.Parameters.Length; i++)
		{
			var parameterType = method.Parameters[i].Type;
			var dependency = parameterType.TypeKind == TypeKind.Dynamic
				? "global::System.Object" : parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			body.AppendLine($$"""
						{{dependency}} argument{{i}};
						try
						{
							argument{{i}} = resolver.Resolve<{{dependency}}>();
						}
						catch (global::System.Exception error)
						{
							throw new global::Injectra.Godot.InjectionException(
								$"Cannot resolve {typeof({{dependency}})} for {typeof({{target}})}.{{method.Name}} on {runtimeType}.", error);
						}
				""");
		}
		var arguments = string.Join(", ", Enumerable.Range(0, method.Parameters.Length).Select(index => $"argument{index}"));
		var namespaceLine = type.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {type.ContainingNamespace.ToDisplayString()};\n\n";
		var source = $$"""
			// <auto-generated/>
			#nullable enable
			#pragma warning disable CS0612, CS0618
			{{namespaceLine}}partial class @{{type.Name}} : global::{{GeneratedContract}}
			{
				bool global::{{GeneratedContract}}.TryInject(global::System.Type runtimeType, global::Injectra.IResolver resolver)
				{
					if (runtimeType != typeof({{target}}))
						return false;

			{{body}}
					try
					{
						this.@{{method.Name}}({{arguments}});
					}
					catch (global::System.Exception error)
					{
						throw new global::Injectra.Godot.InjectionException(
							$"Injection method {typeof({{target}})}.{{method.Name}} on {runtimeType} failed.", error);
					}
					return true;
				}
			}
			""";
		return source.Replace("\r\n", "\n") + "\n";
	}
}

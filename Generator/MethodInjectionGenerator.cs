using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Injectra.Godot.Generation;

/// <summary>Generates direct injection plans in base-to-derived order.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class MethodInjectionGenerator : IIncrementalGenerator
{
	private const string InjectAttribute = "Injectra.InjectAttribute";
	private const string GeneratedContract = "Injectra.Godot.Generation.IGeneratedInjectable";
	private static readonly DiagnosticDescriptor MultipleMethods = new(
		"IGG001", "Multiple injection methods in one class",
		"Type '{0}' declares more than one injection method; declare at most one per class",
		"Usage", DiagnosticSeverity.Error, isEnabledByDefault: true);
	private static readonly DiagnosticDescriptor InvalidMethod = new(
		"IGG002", "Invalid injection method signature",
		"Injection method '{0}' is invalid: {1}",
		"Usage", DiagnosticSeverity.Error, isEnabledByDefault: true);

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// Derived classes need their own plan even without a locally declared [Inject].
		var results = context.SyntaxProvider.CreateSyntaxProvider(
			static (node, _) => node is ClassDeclarationSyntax,
			static (syntax, _) => Describe(syntax));

		context.RegisterSourceOutput(results.Where(result => result.HasValue), static (output, result) =>
		{
			var generated = result!.Value;
			if (generated.Error is { } error)
				output.ReportDiagnostic(error);
			if (generated.Source is { } source)
				output.AddSource(generated.Hint, SourceText.From(source, new UTF8Encoding(false)));
		});
	}

	private static (string Hint, string? Source, Diagnostic? Error)? Describe(GeneratorSyntaxContext syntax)
	{
		var declaration = (ClassDeclarationSyntax)syntax.Node;
		if (syntax.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type)
			return null;
		var first = type.DeclaringSyntaxReferences[0];
		if (first.SyntaxTree != declaration.SyntaxTree || first.Span != declaration.Span)
			return null;

		var compilation = syntax.SemanticModel.Compilation;
		var attribute = compilation.GetTypeByMetadataName(InjectAttribute);
		var nodeType = compilation.GetTypeByMetadataName("Godot.Node");
		if (attribute is null || nodeType is null || compilation.GetTypeByMetadataName(GeneratedContract) is null)
			return null;

		var hierarchy = new List<INamedTypeSymbol>();
		for (var current = type; current is not null; current = current.BaseType)
			hierarchy.Add(current);
		if (!hierarchy.Any(parent => SymbolEqualityComparer.Default.Equals(parent, nodeType)))
			return null;

		var ownMethods = InjectionMethods(type, attribute);
		if (ownMethods.Length > 1)
			return ("", null, Diagnostic.Create(MultipleMethods, declaration.Identifier.GetLocation(), type.ToDisplayString()));
		if (ownMethods.Length == 1 && InjectionSignature.Validate(ownMethods[0]) is { } invalid)
			return ("", null, Diagnostic.Create(InvalidMethod, invalid.Location, ownMethods[0].ToDisplayString(), invalid.Reason));

		if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || type.ContainingType is not null
			|| type.IsGenericType || type.IsAbstract || type.IsStatic || type.IsFileLocal)
			return null;

		var methods = new List<IMethodSymbol>();
		var virtualSlots = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
		hierarchy.Reverse();
		foreach (var parent in hierarchy)
		{
			if (!SymbolEqualityComparer.Default.Equals(parent.ContainingAssembly, compilation.Assembly))
			{
				// Framework bases do not declare Injectra methods; custom external bases require reflection.
				if (parent.ContainingAssembly.Name is "GodotSharp" or "System.Private.CoreLib" or "System.Runtime")
					continue;
				return null;
			}

			var declared = InjectionMethods(parent, attribute);
			if (declared.Length > 1 || declared.Any(method => !Supported(method)))
				return null;
			foreach (var method in declared)
			{
				var root = method;
				while (root.OverriddenMethod is { } overridden)
					root = overridden;
				if (method.IsVirtual || method.IsOverride || method.IsAbstract)
				{
					if (virtualSlots.TryGetValue(root.OriginalDefinition, out var index))
					{
						methods[index] = method;
						continue;
					}
					virtualSlots.Add(root.OriginalDefinition, methods.Count);
				}
				methods.Add(method);
			}
		}
		return (type.ToDisplayString() + ".Injectra.g.cs", InjectionSourceEmitter.Emit(type, methods), null);
	}

	private static IMethodSymbol[] InjectionMethods(INamedTypeSymbol type, INamedTypeSymbol attribute) => type.GetMembers()
		.OfType<IMethodSymbol>().Where(method => method.PartialDefinitionPart is null && IsInjection(method, attribute)).ToArray();

	private static bool IsInjection(IMethodSymbol method, INamedTypeSymbol attribute)
	{
		for (var current = method; current is not null; current = current.OverriddenMethod)
			if (current.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute)))
				return true;
		return false;
	}

	private static bool Supported(IMethodSymbol method) => InjectionSignature.Validate(method) is null
		&& method.MethodKind == MethodKind.Ordinary && !method.IsPartialDefinition && method.PartialDefinitionPart is null
		&& !method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute")
		&& !PreventsDirectCall(method) && !PreventsDirectCall(method.ContainingType);

	private static bool PreventsDirectCall(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
		attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExperimentalAttribute"
		|| (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute"
			&& ((attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value is true)
				|| attribute.NamedArguments.Any(argument => argument.Key == "DiagnosticId" && argument.Value.Value is string id && id.Length != 0))));
}

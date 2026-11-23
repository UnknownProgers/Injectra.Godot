using System.Linq;
using Microsoft.CodeAnalysis;

namespace Injectra.Godot.Generation;

// Signature validity is independent of whether this generator can emit a direct call for the target type.
internal static class InjectionSignature
{
	internal static (Location Location, string Reason)? Validate(IMethodSymbol method)
	{
		var location = method.Locations.FirstOrDefault() ?? Location.None;
		if (method.DeclaredAccessibility != Accessibility.Public)
			return (location, "the method must be public");
		if (method.IsStatic)
			return (location, "the method must be an instance method");
		if (!method.ReturnsVoid)
			return (location, "the return type must be void");
		var implementation = method.PartialImplementationPart ?? method;
		if (implementation.IsAsync || HasAsyncStateMachine(method) || HasAsyncStateMachine(implementation))
			return (implementation.Locations.FirstOrDefault() ?? location, "the method must be synchronous; async void is unsupported");
		if (method.IsGenericMethod)
			return (location, "generic injection methods are unsupported");
		if (method.IsVararg)
			return (location, "variable argument lists are unsupported");

		foreach (var parameter in method.Parameters)
		{
			var parameterLocation = parameter.Locations.FirstOrDefault() ?? location;
			if (parameter.RefKind != RefKind.None)
				return (parameterLocation, $"parameter '{parameter.Name}' must not use ref, out or in");
			if (parameter.Type.IsRefLikeType)
				return (parameterLocation, $"parameter '{parameter.Name}' must not be a ref-struct type");
			if (parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
				return (parameterLocation, $"parameter '{parameter.Name}' must not be a pointer or function pointer");
		}
		return null;
	}

	private static bool HasAsyncStateMachine(IMethodSymbol method) => method.GetAttributes().Any(attribute =>
		attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
}

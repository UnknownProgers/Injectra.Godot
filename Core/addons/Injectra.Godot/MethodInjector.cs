using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Injectra.Godot.Generation;

namespace Injectra.Godot;

/// <summary>Injects explicitly marked methods using one resolver. Intended for use on the Godot main thread.</summary>
internal sealed class MethodInjector
{
	private readonly IResolver _resolver;
	private readonly Dictionary<Type, InjectionMethod[]> _methods = [];

	/// <summary>Creates an injector bound to the supplied resolver.</summary>
	/// <exception cref="ArgumentNullException">The resolver is null.</exception>
	public MethodInjector(IResolver resolver)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		_resolver = resolver;
	}

	/// <summary>Invokes injection methods on one object, with base methods before derived methods.</summary>
	/// <remarks>
	/// Only public instance synchronous void methods without generic or by-reference parameters are supported.
	/// Each declaring class may have at most one injection method. Overrides run once, with or without a repeated attribute.
	/// Every call performs injection again.
	/// Resources referenced by the object are not traversed. Earlier method calls are not rolled back on failure.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The target is null.</exception>
	/// <exception cref="ObjectDisposedException">The target is a freed Godot object.</exception>
	/// <exception cref="InjectionException">A class declares multiple injection methods, a method is unsupported, a dependency cannot be resolved, or a method throws.</exception>
	public void Inject(object target)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (target is GodotObject godotObject && !GodotObject.IsInstanceValid(godotObject))
			throw new ObjectDisposedException(target.GetType().FullName);

		var type = target.GetType();
		if (target is IGeneratedInjectable generated && generated.TryInject(type, _resolver))
			return;

		if (!_methods.TryGetValue(type, out var methods))
		{
			methods = Prepare(type);
			_methods.Add(type, methods);
		}

		foreach (var injection in methods)
		{
			var arguments = new object[injection.Dependencies.Length];
			for (var i = 0; i < arguments.Length; i++)
			{
				try
				{
					arguments[i] = _resolver.Resolve(injection.Dependencies[i]);
				}
				catch (Exception error)
				{
					throw new InjectionException($"Cannot resolve {injection.Dependencies[i]} for {injection.Method.DeclaringType}.{injection.Method.Name} on {type}.", error);
				}
			}

			try
			{
				injection.Method.Invoke(target, arguments);
			}
			catch (TargetInvocationException error)
			{
				throw new InjectionException($"Injection method {injection.Method.DeclaringType}.{injection.Method.Name} on {type} failed.", error.InnerException ?? error);
			}
		}
	}

	/// <summary>Injects the root and its descendants in parent-first order.</summary>
	/// <remarks>Does not scan resources or alter the scene tree. Descendant scopes prepare their own containers. Call before AddChild for pre-tree injection.</remarks>
	/// <exception cref="ArgumentNullException">The root is null.</exception>
	/// <exception cref="ObjectDisposedException">A node has been freed.</exception>
	/// <exception cref="InjectionException">Injection fails on a node. Earlier injections are not rolled back.</exception>
	public void InjectTree(Node root)
	{
		ArgumentNullException.ThrowIfNull(root);
		Inject(root);
		foreach (var child in root.GetChildren())
			if (child is InjectraScope scope)
			{
				if (_resolver is not Container owner)
					throw new InvalidOperationException("Preparing a scope subtree requires a container-backed resolver.");

				scope.Prepare(parentContainer: owner);
			}
			else
				InjectTree(child);
	}

	private static InjectionMethod[] Prepare(Type type)
	{
		var methods = new List<InjectionMethod>();
		var virtualMethods = new Dictionary<MethodInfo, int>();
		Visit(type);
		return methods.ToArray();

		void Visit(Type current)
		{
			if (current.BaseType is { } parent)
				Visit(parent);

			var declared = current.GetMethods(BindingFlags.DeclaredOnly
				         | BindingFlags.Instance
				         | BindingFlags.Static
				         | BindingFlags.Public
				         | BindingFlags.NonPublic)
				.Where(method => method.IsDefined(typeof(InjectAttribute), inherit: true)).ToArray();
			if (declared.Length > 1)
				throw new InjectionException($"Type {current} declares more than one injection method; declare at most one per class.");

			foreach (var method in declared)
			{
				var parameters = method.GetParameters();
				if (!method.IsPublic
				    || method.IsStatic
				    || method.ReturnType != typeof(void)
				    || method.ContainsGenericParameters
					|| method.IsDefined(typeof(AsyncStateMachineAttribute), inherit: false)
					|| parameters.Any(parameter => parameter.ParameterType.IsByRef
					    || parameter.ParameterType.IsByRefLike
					    || parameter.ParameterType.IsPointer
					    || parameter.ParameterType.IsFunctionPointer))
					throw new InjectionException($"Unsupported injection method {current}.{method.Name}: "
						+ $"expected a public instance synchronous void method without generic, by-reference, pointer or ref-struct parameters.");

				var injection = new InjectionMethod(method, parameters.Select(parameter => parameter.ParameterType).ToArray());
				if (method.IsVirtual)
				{
					var definition = method.GetBaseDefinition();
					if (virtualMethods.TryGetValue(definition, out var index))
					{
						methods[index] = injection;
						continue;
					}
					virtualMethods.Add(definition, methods.Count);
				}
				methods.Add(injection);
			}
		}
	}

	private sealed record InjectionMethod(MethodInfo Method, Type[] Dependencies);
}

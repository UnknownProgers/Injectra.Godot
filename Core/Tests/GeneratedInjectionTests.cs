using Injectra.Godot.Generation;

namespace Injectra.Godot.Tests;

internal static class GeneratedInjectionTests
{
	internal static void Run()
	{
		GeneratedCallsUseTypedResolution();
		GeneratedCallsUseChildOverrides();
		FailuresPreserveTheirCauses();
		DerivedNodesFallBackWithoutSkippingMethods();
		InjectionNode.Created.Clear();
		global::Godot.GD.Print("PASS: generated method injection, typed resolution, child overrides, errors and inherited fallback.");
	}

	private static void GeneratedCallsUseTypedResolution()
	{
		var dependency = new InjectionDependency();
		var resolver = new TypedResolver(dependency);
		var injector = new MethodInjector(resolver);
		var node = new InjectionNode();
		Check(node is IGeneratedInjectable, "The real Godot build must generate an injection implementation.");

		injector.Inject(node);
		injector.Inject(node);

		Check(ReferenceEquals(node.Dependency, dependency), "The generated call must inject the supplied dependency.");
		Check(resolver.Calls == 2, "Every injection must resolve arguments again through the typed API.");
		node.Free();
		Throws<ObjectDisposedException>(() => injector.Inject(node));
	}

	private static void GeneratedCallsUseChildOverrides()
	{
		var original = new InjectionDependency();
		var replacement = new InjectionDependency();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(original);
		using var root = builder.Build();
		using var child = root.CreateChild(local => local.RegisterInstance(replacement));
		var node = new InjectionNode();

		new MethodInjector(child).Inject(node);

		Check(ReferenceEquals(node.Dependency, replacement), "Generated calls must preserve the requesting child's overrides.");
		node.Free();
	}

	private static void FailuresPreserveTheirCauses()
	{
		using var empty = new ContainerBuilder().Build();
		var node = new InjectionNode();
		var resolutionError = Throws<InjectionException>(() => new MethodInjector(empty).Inject(node));
		Check(resolutionError.InnerException is ResolutionException && resolutionError.Message.Contains("Construct"),
			"Generated resolution errors must preserve their cause and identify the injection method.");

		var resolver = new TypedResolver(new InjectionDependency());
		node.FailInjection = true;
		var methodError = Throws<InjectionException>(() => new MethodInjector(resolver).Inject(node));
		Check(methodError.InnerException is InvalidOperationException { Message: "Deliberate injection failure." },
			"Generated method errors must preserve the original user exception.");
		node.Free();
	}

	private static void DerivedNodesFallBackWithoutSkippingMethods()
	{
		var dependency = new InjectionDependency();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(dependency);
		using var container = builder.Build();
		var node = new GeneratedDerivedProbe();

		Check(!((IGeneratedInjectable)node).TryInject(node.GetType(), container), "An inherited generated plan must refuse an uncovered runtime type.");
		new MethodInjector(container).Inject(node);

		Check(ReferenceEquals(node.Dependency, dependency) && node.AdditionalCalls == 1,
			"Reflection fallback must execute both base and derived injection methods once.");
		node.Free();
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private static T Throws<T>(Action action) where T : Exception
	{
		try
		{
			action();
		}
		catch (T error)
		{
			return error;
		}
		throw new InvalidOperationException($"Expected {typeof(T).Name}.");
	}

	private sealed class TypedResolver(InjectionDependency dependency) : IResolver
	{
		public int Calls { get; private set; }

		public T Resolve<T>()
		{
			Calls++;
			return (T)(object)dependency;
		}

		public object Resolve(Type serviceType) => throw new InvalidOperationException("Generated injection must not use reflection's Type-based resolution.");
		public IReadOnlyList<T> ResolveAll<T>() => throw new NotSupportedException();
		public IReadOnlyList<object> ResolveAll(Type serviceType) => throw new NotSupportedException();
	}
}

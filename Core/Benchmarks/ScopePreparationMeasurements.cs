using Godot;
using Injectra.Godot.Generation;
using Injectra.Lifecycle;

namespace Injectra.Godot.Benchmarks;

internal static partial class ScopePreparationMeasurements
{
	internal static void Run(BenchmarkScope target)
	{
		ContainerBenchmarks.Measure("Node construction only", static () => new Node(), 512, static node => node.Free());
		ContainerBenchmarks.Measure("BenchmarkScope construction only", static () => new BenchmarkScope(), 512, static node => node.Free());
		ContainerBenchmarks.Measure("Core build, 4 registrations", static () => BuildCore(false), 512, static container => container.Dispose());
		ContainerBenchmarks.Measure("Core build + lifecycle + initialize", static () => BuildCore(true), 512, static container => container.Dispose());

		ContainerBenchmarks.Measure("Fresh injector, empty scope injection", () =>
		{
			var injector = new MethodInjector(target.Container);
			injector.Inject(target);
			return injector;
		}, 256);

		var cachedInjector = new MethodInjector(target.Container);
		ContainerBenchmarks.Measure("Reused injector, empty scope injection", () =>
		{
			cachedInjector.Inject(target);
			return cachedInjector;
		}, 100_000);

		ContainerBenchmarks.Measure("Full scope, injection discovery bypassed", static () =>
		{
			var scope = new NoInjectionBenchmarkScope();
			try
			{
				scope.Prepare();
			}
			catch
			{
				scope.Free();
				throw;
			}
			return scope;
		}, 256, static scope => scope.Free());
		ContainerBenchmarks.Measure("Full scope, current implementation", static () =>
		{
			var scope = new BenchmarkScope();
			try
			{
				scope.Prepare();
			}
			catch
			{
				scope.Free();
				throw;
			}
			return scope;
		}, 256, static scope => scope.Free());
	}

	private static Container BuildCore(bool lifecycle)
	{
		var builder = new ContainerBuilder();
		BenchmarkScope.RegisterServices(builder);
		if (lifecycle)
			builder.UseLifecycle(static manager => manager.RegisterPhase<IPhysicsTickable, double>(static (service, delta) => service.PhysicsTick(delta)));
		var container = builder.Build();
		if (lifecycle)
			container.Resolve<InjectraLifecycle>().Initialize();
		return container;
	}

	// Nested to keep this manual control outside the generator's supported targets.
	internal partial class NoInjectionBenchmarkScope : BenchmarkScope, IGeneratedInjectable
	{
		bool IGeneratedInjectable.TryInject(Type runtimeType, IResolver resolver) => runtimeType == typeof(NoInjectionBenchmarkScope);
	}
}

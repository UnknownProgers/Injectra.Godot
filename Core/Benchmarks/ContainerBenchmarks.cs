using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Godot;

namespace Injectra.Godot.Benchmarks;

public partial class ContainerBenchmarks : Node
{
	public override void _Ready() => Callable.From(Start).CallDeferred();

	private void Start()
	{
		try
		{
			// Run an independent root, without the demo project's registrations or lifecycle.
			GetNodeOrNull<Node>("/root/ProjectScope")?.Free();
			Run();
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}

	private static void Run()
	{
		var selected = OS.GetCmdlineUserArgs().FirstOrDefault() ?? "all";
		if (selected is not ("all" or "singleton" or "transient" or "factory" or "build" or "preparation"))
			throw new ArgumentException("Select all, singleton, transient, factory, build or preparation after --.");

		var scope = new BenchmarkScope();
		scope.Prepare();
		try
		{
			var container = scope.Container;
			var singleton = container.Resolve<BenchmarkDependency>();
			if (!ReferenceEquals(singleton, container.Resolve<BenchmarkDependency>())
			    || ReferenceEquals(container.Resolve<BenchmarkConsumer>(), container.Resolve<BenchmarkConsumer>()))
				throw new InvalidOperationException("Unexpected service lifetime.");

			using var scene = GD.Load<PackedScene>("res://Benchmarks/InjectionProbe.tscn");
			var factory = container.Resolve<SceneFactory>();
			var probe = factory.Instantiate<InjectionProbe>(scene);
			try
			{
				if (!ReferenceEquals(probe.First, singleton) || !ReferenceEquals(probe.Second, singleton)
				    || !ReferenceEquals(probe.Third, singleton) || probe.Calls != 1 || probe.IsInsideTree())
					throw new InvalidOperationException("Scene factory injection check failed.");
			}
			finally
			{
				probe.Free();
			}

			GD.Print($"Runtime: {RuntimeInformation.FrameworkDescription}; Godot: {Engine.GetVersionInfo()["string"]}; {RuntimeInformation.ProcessArchitecture}");
			GD.Print("7 samples; median; main thread. Scope includes node creation, addon registrations and lifecycle setup. Cleanup excluded.");
			GD.Print("| Case | ns/op | Managed B/op | Min ns | Max ns |");
			GD.Print("|---|---:|---:|---:|---:|");
			if (selected == "preparation")
				ScopePreparationMeasurements.Run(scope);
			if (selected is "all" or "singleton")
				Measure("Singleton resolve", () => container.Resolve(typeof(BenchmarkDependency)), 100_000);
			if (selected is "all" or "transient")
				Measure("Transient resolve, 3 singleton deps", () => container.Resolve(typeof(BenchmarkConsumer)), 100_000);
			if (selected is "all" or "factory")
			{
				Measure("PackedScene instantiate, no DI", () => scene.Instantiate<InjectionProbe>(), 512,
					static node => node.Free());
				Measure("SceneFactory instantiate + inject root", () => factory.Instantiate<InjectionProbe>(scene), 512,
					static node => node.Free());
			}
			if (selected is "all" or "build")
				Measure("Scope create + prepare, 4 registrations", static () =>
				{
					var created = new BenchmarkScope();
					try
					{
						created.Prepare();
					}
					catch
					{
						created.Free();
						throw;
					}
					return created;
				}, 256, static created => created.Free());
			GD.Print("PASS: singleton/transient lifetimes and scene injection verified.");
		}
		finally
		{
			scope.Free();
		}
	}

	internal static void Measure<T>(string name, Func<T> operation, int iterations, Action<T>? cleanup = null) where T : class
	{
		for (var i = 0; i < 2_000; i++)
		{
			var value = operation();
			cleanup?.Invoke(value);
		}

		var times = new double[7];
		var allocations = new double[7];
		var results = new T[iterations];
		for (var sample = 0; sample < times.Length; sample++)
		{
			var completed = 0;
			try
			{
				var allocated = GC.GetAllocatedBytesForCurrentThread();
				var start = Stopwatch.GetTimestamp();
				for (; completed < iterations; completed++)
					results[completed] = operation();
				var elapsed = Stopwatch.GetTimestamp() - start;
				allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
				times[sample] = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
			}
			finally
			{
				for (var i = 0; i < completed; i++)
				{
					cleanup?.Invoke(results[i]);
					results[i] = null!;
				}
			}
		}
		Array.Sort(times);
		Array.Sort(allocations);
		GD.Print(string.Create(CultureInfo.InvariantCulture,
			$"| {name} | {times[3]:F2} | {allocations[3]:F2} | {times[0]:F2} | {times[^1]:F2} |"));
	}
}

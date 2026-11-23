using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Injectra.Godot.Generation;

namespace Injectra.Godot.Benchmarks;

public partial class InjectionBenchmarks : Node
{
	private const int Samples = 7;
	private const int WarmIterations = 100_000;
	private const int ColdIterations = 1_000;

	public override void _Ready()
	{
		try
		{
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
		if (selected is not ("all" or "warm" or "cold"))
			throw new ArgumentException("Select all, warm or cold after --.");

		var builder = new ContainerBuilder();
		var dependency = new BenchmarkDependency();
		builder.RegisterInstance(dependency);
		using var container = builder.Build();
		var generated = new InjectionProbe();
		var reflection = new ReflectionProbe();
		try
		{
			if (!((IGeneratedInjectable)generated).TryInject(generated.GetType(), container)
				|| ((IGeneratedInjectable)reflection).TryInject(reflection.GetType(), container))
				throw new InvalidOperationException("Expected generated and reflection paths respectively.");

			GD.Print($"Runtime: {RuntimeInformation.FrameworkDescription}; Godot: {Engine.GetVersionInfo()["string"]}; {RuntimeInformation.ProcessArchitecture}");
			GD.Print($"3 singleton arguments; {Samples} samples; median; main thread. Cold = new injector, not process startup.");
			GD.Print("| Case | ns/op | B/op | Min ns | Max ns |");
			GD.Print("|---|---:|---:|---:|---:|");
			if (selected is "all" or "warm")
			{
				var generatedInjector = new MethodInjector(container);
				var reflectionInjector = new MethodInjector(container);
				Measure("Generated / warm", () => generatedInjector.Inject(generated), WarmIterations);
				Measure("Reflection / warm", () => reflectionInjector.Inject(reflection), WarmIterations);
			}
			if (selected is "all" or "cold")
			{
				Measure("Generated / cold", () => new MethodInjector(container).Inject(generated), ColdIterations);
				Measure("Reflection / cold", () => new MethodInjector(container).Inject(reflection), ColdIterations);
			}
			if (!ReferenceEquals(generated.First, dependency) || !ReferenceEquals(generated.Second, dependency)
				|| !ReferenceEquals(generated.Third, dependency) || !ReferenceEquals(reflection.First, dependency)
				|| !ReferenceEquals(reflection.Second, dependency) || !ReferenceEquals(reflection.Third, dependency)
				|| generated.Calls == 0 || reflection.Calls == 0)
				throw new InvalidOperationException("Injection correctness check failed.");
			GD.Print("PASS: generated/reflection paths and injected dependencies verified.");
		}
		finally
		{
			generated.Free();
			reflection.Free();
		}
	}

	private static void Measure(string name, Action action, int iterations)
	{
		for (var i = 0; i < 10_000; i++)
			action();

		var times = new double[Samples];
		var allocations = new double[Samples];
		for (var sample = 0; sample < Samples; sample++)
		{
			var allocated = GC.GetAllocatedBytesForCurrentThread();
			var start = Stopwatch.GetTimestamp();
			for (var i = 0; i < iterations; i++)
				action();
			var elapsed = Stopwatch.GetTimestamp() - start;
			allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
			times[sample] = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
		}
		Array.Sort(times);
		Array.Sort(allocations);
		GD.Print(string.Create(CultureInfo.InvariantCulture,
			$"| {name} | {times[Samples / 2]:F2} | {allocations[Samples / 2]:F2} | {times[0]:F2} | {times[^1]:F2} |"));
	}

	// Nested targets use the real reflection fallback. The inherited generated method
	// rejects the different runtime type; the injection body and dependencies are identical.
	public partial class ReflectionProbe : InjectionProbe;
}

using Godot;

namespace Injectra.Godot.Benchmarks;

public partial class InjectionProbe : Node
{
	public object? First { get; private set; }
	public object? Second { get; private set; }
	public object? Third { get; private set; }
	public int Calls { get; private set; }

	[Inject]
	public void Construct(BenchmarkDependency first, BenchmarkDependency second, BenchmarkDependency third)
	{
		First = first;
		Second = second;
		Third = third;
		Calls++;
	}
}
public sealed class BenchmarkDependency;

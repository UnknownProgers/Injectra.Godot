namespace Injectra.Godot.Benchmarks;

public partial class BenchmarkScope : InjectraScope
{
	protected override void Configure(ContainerBuilder builder)
	{
		builder.Register<BenchmarkDependency>(Lifetime.Singleton);
		builder.Register<BenchmarkServiceB>(Lifetime.Singleton);
		builder.Register<BenchmarkServiceC>(Lifetime.Singleton);
		builder.Register<BenchmarkConsumer>(Lifetime.Transient);
	}
}

public sealed class BenchmarkServiceB;
public sealed class BenchmarkServiceC;

// Match the core benchmark's 24-byte consumer: resolve all arguments, retain no fields.
public sealed class BenchmarkConsumer
{
	public BenchmarkConsumer(BenchmarkDependency first, BenchmarkServiceB second, BenchmarkServiceC third) { }
}

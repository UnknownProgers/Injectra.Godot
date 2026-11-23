# Method injection benchmarks

Short microbenchmarks running on the Godot main thread with real nodes, the actual
`MethodInjector`, generated injection code and an Injectra container.
No runtime injector changes or simulated Godot types are required.

## Run

From the repository root:

```sh
dotnet build Core/Core.csproj --configuration Debug -p:Optimize=true
DOTNET_TieredCompilation=0 godot --headless --path Core res://Benchmarks/InjectionBenchmarks.tscn
```

Use the Godot **.NET** executable. `godot` above stands for its local path.
The editor configuration keeps editor-only addon APIs available; `Optimize=true`
enables compiler optimizations. Tiered compilation is disabled for reproducible
steady-state comparisons without JIT tier changes during short samples.
These numbers do not describe the initial JIT/startup cost of a normal game.

Select a group after `--`:

```sh
DOTNET_TieredCompilation=0 godot --headless --path Core res://Benchmarks/InjectionBenchmarks.tscn -- warm
DOTNET_TieredCompilation=0 godot --headless --path Core res://Benchmarks/InjectionBenchmarks.tscn -- cold
```

The default is `all`. Each case has 10,000 warm-up calls and seven samples:
100,000 operations per warm sample, 1,000 per cold sample. The table reports
median time, median managed bytes allocated on the current thread, and min/max
sample time. This is an in-engine stopwatch harness, not BenchmarkDotNet;
there are no statistical confidence intervals or subprocess isolation.

## Cases

- **Warm:** reuse one injector, including its prepared reflection plan.
- **Cold:** create a new injector and inject once per operation. Includes injector
  allocation and reflection plan discovery. The process, JIT, runtime metadata
  and container are already warm; this is not a process-first-call benchmark.

Both paths call the same inherited `Construct` body with three references to one
registered singleton. Generated injection uses typed resolution; fallback uses
Type-based resolution, as in production. Node creation, container build, scene
traversal and destruction are outside the measured loop. Native validity checks
inside `MethodInjector.Inject` remain included. Delegate invocation and loop
cost remain included in both paths; no baseline subtraction is applied.

`InjectionProbe` is generated. Its nested subclass deliberately falls outside
the generator's supported target shapes and uses the reflection fallback. Its
inherited generated implementation rejects the different runtime type. Fallback
therefore also includes this failed fast-path check and one extra inheritance
level during cold plan discovery. The benchmark verifies both path selections
and dependency identities, then frees both nodes.

## Earlier local-source baseline — 2026-11-23

AMD Ryzen 7 9800X3D, Godot 4.6.1 stable Mono, .NET 11.0.0, Linux x64, optimized editor assembly,
`DOTNET_TieredCompilation=0`. Last of two consecutive runs:

| Case | ns/op | B/op | Min ns | Max ns |
|---|---:|---:|---:|---:|
| Generated / warm | 80.29 | 0 | 79.83 | 81.78 |
| Reflection / warm | 88.28 | 48 | 87.28 | 94.50 |
| Generated / cold | 103.66 | 112 | 101.46 | 105.88 |
| Reflection / cold | 30,987.25 | 12,168 | 30,482.29 | 32,001.11 |

The preceding run measured 80.74 / 91.66 ns warm and 102.78 / 31,053.42 ns cold.
This fixture shows the cost of preparing a reflection plan for a Node hierarchy;
it does not predict full scene-instantiation speed or every inheritance shape.

## Addon container and scene scenarios

```sh
DOTNET_TieredCompilation=0 godot --headless --path Core res://Benchmarks/ContainerBenchmarks.tscn
```

Select `singleton`, `transient`, `factory` or `build` after `--` to run one case.
Same optimized build command and environment as above. The demo project Autoload
is freed before measurement, so the benchmark scope is an independent root.

Four user registrations: three singleton services and a transient consumer with
three constructor dependencies. The consumer retains no fields, matching the
core benchmark's 24-byte object. Resolve uses the Type-based API, as in the core
comparison. The scope additionally installs its own scope instance, SceneFactory
and lifecycle. No Grove or Microsoft DI dependencies are added.

- Singleton/transient: resolve from one prepared scope's container.
- Factory: instantiate a loaded, single-node PackedScene through the scope's
  SceneFactory and inject its root using generated method injection. Three
  arguments reference the same singleton, as in the injection benchmark above.
- Build: create a fresh BenchmarkScope and call Prepare, including native Node
  creation, registrations, root lifecycle setup and root injection discovery.
  The scope stays outside SceneTree: EnterTree/Ready/child callbacks are excluded.

Each case has 2,000 warm-up operations and seven samples. Resolve uses 100,000
operations per sample, factory 512, build 256. Results are retained in a
preallocated array until each sample ends, preventing discarded results and
allowing nodes/scopes to be freed **outside** the timed/allocated region. Delegate
calls and result-array writes remain included; array allocation, scene loading,
container warm-up and cleanup are excluded. Bytes are **managed** allocations;
native Godot memory is not measured. JIT compilation is warmed before sampling.
This harness is not equivalent to BenchmarkDotNet's measurement overhead.

Earlier local-source baseline on the same machine/environment, 2026-11-23, first successful full run:

| Case | Median ns/op | Managed B/op | Min ns | Max ns |
|---|---:|---:|---:|---:|
| Singleton resolve | 15.42 | 0 | 15.17 | 16.99 |
| Transient resolve, 3 singleton dependencies | 64.71 | 24 | 63.34 | 65.91 |
| SceneFactory instantiate + inject root | 1,605.90 | 320 | 1,585.57 | 1,730.77 |
| Scope create + prepare, 4 user registrations | 39,014.14 | 17,664 | 38,426.82 | 39,620.98 |

Second full run: 15.36 / 64.41 / 1,624.99 / 60,077.63 ns respectively, with
identical allocation counts. The build case had a wider 38,840.89–65,125.72 ns
sample range. A separate `-- build` run measured 38,504.63 ns median
(38,031.85–48,833.07 ns). Scope setup is noticeably noisier than warm resolves.

These results describe the addon in an optimized Godot editor build, with tiered
compilation disabled. They must not be read as a regression against the core
README's BenchmarkDotNet table: the harness/JIT settings differ, and scene
instantiation and full scope setup do substantially more work than a managed
factory and ContainerBuilder.Build. Use one harness and matching configurations
for a direct comparison.

## Published NuGet baseline — 2026-11-23

The addon and generator tests now unconditionally reference **Injectra
0.7.0-alpha.1** from NuGet. There is no local Core/Lifecycle project substitution.
The package includes `analyzers/dotnet/cs/Injectra.Generator.dll` automatically.
Godot method generation still comes from this repository's generator project.
Build and measurement commands remain as documented above.

| Addon scenario | Median ns/op | Managed B/op | Min ns | Max ns |
|---|---:|---:|---:|---:|
| Singleton resolve | 9.02 | 0 | 8.83 | 10.26 |
| Transient resolve, 3 singleton dependencies | 19.32 | 24 | 19.02 | 21.76 |
| SceneFactory instantiate + inject root | 1,557.66 | 320 | 1,551.08 | 1,714.66 |
| Scope create + prepare, 4 user registrations | 37,647.18 | 17,664 | 37,509.17 | 39,858.07 |

| Injection scenario | Median ns/op | Managed B/op | Min ns | Max ns |
|---|---:|---:|---:|---:|
| Generated / warm | 46.63 | 0 | 43.09 | 50.47 |
| Reflection / warm | 68.63 | 48 | 67.31 | 69.27 |
| Generated / cold | 65.06 | 112 | 64.27 | 69.07 |
| Reflection / cold | 31,206.12 | 12,168 | 30,316.94 | 31,539.74 |

These replace the local-source numbers above as the baseline for the published
Core dependency. The difference alone does not isolate the cause: dependency
build configuration and binary version are not controlled independently here.

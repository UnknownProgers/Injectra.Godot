# Injectra.Godot.Generation

Compiler-only method injection for the Injectra Godot addon. This package is a Roslyn analyzer, not a runtime library.
It requires the addon sources containing `IGeneratedInjectable`; ordinary Injectra services still use constructor injection.

The first stage generates direct calls for top-level, nongeneric partial Node classes with one supported public `[Inject]` method.
The addon automatically selects the generated path. Inheritance, multiple injection methods and other unsupported shapes retain reflection fallback.
Generated calls use the current resolver and preserve injection error causes.

The generator is built separately from the game; only its generated C# code becomes part of the game assembly.
NuGet publication is not configured yet. See the [addon repository](https://github.com/UnknownProgers/Injectra.Godot) for development and runtime setup.

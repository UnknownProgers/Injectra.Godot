# Injectra.Godot.Generation

Compiler-only method injection for the Injectra Godot addon. This package is a Roslyn analyzer, not a runtime library.
It requires the addon sources containing `IGeneratedInjectable`; ordinary Injectra services still use constructor injection.

The generator emits inherited injection plans for top-level, nongeneric partial Node classes.
Each declaring class may have at most one injection method, otherwise compiler error `IGG001` is reported.
Overrides inherit `[Inject]`. Repeating the attribute does not change behavior: the virtual slot runs once.
An override plus another local injection method is a duplicate whether or not the override repeats the attribute.
Plans preserve base-to-derived slot order; hidden methods have separate calls through their declaring types.
The addon automatically selects the generated path. Eligible types without injection methods receive an empty plan, avoiding reflection
discovery. Every plan accepts only its exact runtime type. Nested/generic target classes and custom external bases retain reflection
fallback.
Generated calls use the current resolver and preserve injection error causes.

The generator is built separately from the game; only its generated C# code becomes part of the game assembly.
Requires a C# Godot .NET project with the matching addon sources installed. The current addon targets .NET 11.
Install alongside the matching addon release:

```xml
<PackageReference Include="Injectra.Godot.Generation" Version="0.9.5-alpha.1" PrivateAssets="all" />
```

No manual analyzer references are needed. Install `Injectra` separately for the runtime container and constructor generation.
See the [addon repository](https://github.com/UnknownProgers/Injectra.Godot) for runtime setup.

`IGG002` rejects invalid local Node injection signatures: non-public/static, non-void, async, generic methods,
and by-reference, ref-struct, pointer/function-pointer or variable-argument parameters.
It applies to abstract bases, overrides and unsupported target shapes too. Valid unsupported shapes continue to use reflection fallback.
This package targets Node method injection; ordinary C# services use the base Injectra constructor generator.

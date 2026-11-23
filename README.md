<p align="center">
  <img src="Core/addons/injectra/icons/icon.png" alt="Injectra.Godot" width="120"/>
</p>

<h1 align="center">Injectra.Godot</h1>

<p align="center">A lightweight C# dependency injection addon for Godot, built on Injectra.</p>

<p align="center">
  <a href="https://github.com/UnknownProgers/Injectra.Godot/releases">
    <img src="https://img.shields.io/github/v/release/UnknownProgers/Injectra.Godot?include_prereleases&amp;label=release" alt="Release">
  </a>
  <a href="https://www.nuget.org/packages/Injectra.Godot.Generation/">
    <img src="https://img.shields.io/nuget/v/Injectra.Godot.Generation?label=nuget">
  </a>
</p>

Register your services in a scope, then receive them in your nodes through `[Inject]` methods.
The addon creates the container, injects selected nodes, and closes it when the scope leaves the scene tree.
[Injectra](https://github.com/UnknownProgers/Injectra) provides the engine-independent container.

**Alpha.** Tested on Linux with Godot **4.6.1 .NET**, **.NET 11**, and **C# 15**.
The current Injectra package requires .NET 11; a project targeting .NET 8 must be retargeted before installation.
This addon is for C# projects using the .NET edition of Godot.

## What it provides

- Project, scene and nested scopes, with dependency overrides.
- Resource installers configured in the inspector, plus registrations from code.
- Method injection with generated calls and a reflection fallback.
- A `SceneFactory` that injects scenes before adding them to the tree.
- A scene registry for creating scenes by their C# node type.
- Initialization, frame and physics callbacks for registered services.
- An optional editor panel for inspecting running containers.

## Install

1. Download `Injectra.Godot-<version>.zip` from [GitHub Releases](https://github.com/UnknownProgers/Injectra.Godot/releases).
2. Extract it into your game so the directory is **`res://addons/injectra/`**.
3. Add both NuGet packages to your game's `.csproj`:

```xml
<ItemGroup>
	<PackageReference Include="Injectra" Version="0.7.0-alpha.1" />
	<PackageReference Include="Injectra.Godot.Generation" Version="0.9.5-alpha.1" PrivateAssets="all" />
</ItemGroup>
```

4. Run `dotnet build MyGame.csproj`, then return to Godot.

When upgrading from the old addon directory, disable Injectra Diagnostics first and remove the old addon folder.
Install the new `addons/injectra` directory, rebuild, then enable diagnostics again. Update any manually saved script paths.

Use the generator version matching your addon release. `Injectra` supplies the container and constructor generator;
`Injectra.Godot.Generation` supplies the compiler generator for `[Inject]` methods on nodes.
The addon itself is distributed as source files in the ZIP. Both generators are connected automatically by NuGet.

You do not need to copy DLLs, create a `MethodInjector`, or enable an editor plugin to use DI.
Enable **Injectra Diagnostics** only if you want the debugger panel described below.

## Your first scene

Create a service in `GameSession.cs`:

```csharp
using System;

public sealed class GameSession : IDisposable
{
	public Guid Id { get; } = Guid.NewGuid();
	public void Dispose() { }
}
```

Create `GameInstaller.cs` to register it:

```csharp
using Godot;
using Injectra;
using Injectra.Godot;

[GlobalClass]
public partial class GameInstaller : ResourceInstaller
{
	public override void Install(ContainerBuilder builder)
	{
		builder.Register<GameSession>(Lifetime.Singleton);
	}
}
```

Receive the service in `GameController.cs`:

```csharp
using Godot;
using Injectra;

public partial class GameController : Node
{
	private GameSession _session = null!;

	[Inject]
	public void Construct(GameSession session)
	{
		_session = session;
	}

	public override void _Ready() => GD.Print($"Session: {_session.Id}");
}
```

Build the project so Godot discovers the scripts. Create this scene:

```text
GameScope : InjectraScope          Injection = Tree
└── GameController                 GameController.cs
```

In `GameScope`'s inspector:

1. Add a `GameInstaller` resource to **Installers**.
2. Set **Injection** to **Tree**.
3. Run the scene. The controller prints its injected session ID.

**The default is `Root`.** It injects only the scope node itself. Use `Tree` to inject descendants, as in this example.
The addon does not search parents or siblings for injection targets.

The singleton is created when first requested. Closing this scope disposes its owned services, including `GameSession`.

## When does injection happen?

For a scene opened by Godot:

```text
Scope._EnterTree()
→ resource installers, in inspector order
→ Configure(builder)
→ container preparation
→ injection into selected nodes
→ lifecycle initialization
→ descendant _EnterTree()
→ _Ready(), children before parents
```

The scope's own `_EnterTree()` has already started. Selected descendants receive dependencies before their `_EnterTree()` and `_Ready()`.

For a scene created through `SceneFactory`:

```text
PackedScene.Instantiate()
→ prepare containers for selected scopes
→ inject selected nodes
→ initialize their lifecycle participants
→ AddChild(), if a parent was supplied
→ _EnterTree()
→ _Ready()
```

Here injection happens **before `_EnterTree()`**, including on the scene root.
A prepared scope does not build or inject again when it enters the tree.
If a new scope is created during a lifecycle callback, its initialization waits until that traversal finishes;
injection still completes immediately, and initialization precedes its first tick.

`Initialize()` does not mean the scene is ready or attached to the tree. Put tree-dependent startup in Godot callbacks.
Installers only declare registrations: do not build the container or spawn scenes inside them.
Services are created when resolved, unless registered with `.NonLazy()` or requested during lifecycle preparation.

## Project, scene and child scopes

Use a project scope for services that must survive scene changes:

1. Create a scene with an `InjectraProjectScope` root.
2. Assign its installers.
3. Add it in **Project Settings → Globals → Autoload**, before Autoloads that depend on it.

Only one project scope can be active.

```text
ProjectScope [Autoload]          persistent services
└── LevelScope                  level services
    ├── InventoryScope          local services or overrides
    └── CombatScope             local services or overrides
```

This is the **container hierarchy**. The Autoload and the level scene are siblings in Godot's actual scene tree.

| How a scope is created | Its parent container |
|---|---|
| Through `SceneFactory` | The factory's container |
| By Godot, under another scope | The nearest ancestor scope's container |
| Without a scope ancestor | The active project scope's container |
| Without either | A new independent root |

Children use parent registrations unless they register their own replacement.
A parent singleton remains owned by the parent and keeps its original dependencies.
An inherited transient is created using the requesting child's overrides.

Each scope gets its own `SceneFactory`. During `Tree` injection, a nested scope prepares its own branch with its own container.
With `Root` injection, descendant scopes are not searched; they prepare when Godot enters them into the tree.

Closing a child disposes its owned services and descendants while preserving parent services and sibling branches.
A scope that leaves the tree is permanently closed. Create a new scope to load the level again.

## Create scenes with SceneFactory

Receive the factory through injection:

```csharp
using Godot;
using Injectra;
using Injectra.Godot;

public partial class Spawner : Node
{
	[Export] public PackedScene PlayerScene { get; set; } = null!;
	private SceneFactory _scenes = null!;

	[Inject]
	public void Construct(SceneFactory scenes) => _scenes = scenes;

	public Node Spawn() => _scenes.Instantiate<Node>(PlayerScene, this);
}
```

Select the spawner for injection through its scope or a factory-created scene. The factory injects the new scene's root by default.
To include descendants, pass `injection: SceneInjection.Tree`.
Omit the parent argument to receive an injected scene that is still detached.

The supplied parent determines placement, not which container resolves dependencies.
A scene whose root is an `InjectraScope` creates a child container and uses that scope's own **Injection** setting.
Successful instances belong to the caller or the Godot scene tree. If injection fails, the factory frees the new subtree.
Calling `PackedScene.Instantiate()` directly does not inject ordinary nodes.

### Create a scene by C# type

Create a `InjectraSceneRegistry` resource and assign it to a scope's **SceneRegistry** property, usually on the project scope.
Add `InjectraSceneRegistration` resources to **Entries**. For each entry, select the node's C# **Script** and its **Scene**.

```csharp
var player = scenes.Instantiate<Player>(parent);
```

`Player` is the C# type of that scene's root. Mappings use exact types.
Children inherit mappings and can override them in their own registry.
Mappings are fixed during scope preparation; later resource edits do not update a prepared factory.
Duplicate or incomplete entries fail during preparation. Missing mappings or incompatible roots fail during creation.
If multiple scenes use the same script, register one default and pass a `PackedScene` explicitly for the others.

## Injection rules and code generation

`[Inject]` comes from the base `Injectra` package. Apply it to a **public instance synchronous `void` method**.
There is at most one injection method per declaring class. Different inheritance levels may each introduce one.

- Virtual overrides inherit `[Inject]`; repeating it is allowed. The override runs once in the original base method's slot.
- An override plus another injection method in the same class is a duplicate.
- Generic methods, `async`, `ref`/`out`/`in`, ref-struct, pointer and variable-argument parameters are unsupported.
- Fields and properties are not injection targets. Use constructors for ordinary C# services.

The generator reports duplicate methods as **IGG001** and invalid signatures as **IGG002**.
Runtime fallback validates the complete plan before invoking injection methods.

Top-level, nongeneric `partial` Node classes get generated direct calls, including supported inherited methods.
Types with no injection methods get an empty plan, avoiding reflection discovery.
Each plan accepts only its exact runtime type, so an uncovered descendant cannot silently miss its own methods.

Valid nested/generic targets, custom external base classes, and methods the generator cannot call directly use reflection fallback.
Each injector caches reflection plans by runtime type. Discovery skips Godot's framework classes but checks user ancestors.
The addon still works without the generator through this fallback.

Referenced resources are not traversed, and `RegisterInstance` does not automatically invoke `[Inject]`.
Automatic resource injection is not currently exposed by the scope API.

## Service lifecycle

The root scope enables one `InjectraLifecycle` for its entire container hierarchy.
Child scopes join it automatically. Register the lifecycle interfaces explicitly:

```csharp
using Injectra;
using Injectra.Godot;
using Injectra.Lifecycle;

public sealed class Simulation : IInitializable, IFrameTickable, IPhysicsTickable
{
	public void Initialize() { }
	public void FrameTick(double deltaTime) { }
	public void PhysicsTick(double deltaTime) { }
}

// In your installer:
// builder.Register<Simulation>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
```

| Interface | When it runs |
|---|---|
| `Injectra.Lifecycle.IInitializable` | Once, after branch preparation |
| `Injectra.Lifecycle.IFrameTickable` | Root scope `_Process(delta)` |
| `Injectra.Godot.IPhysicsTickable` | Root scope `_PhysicsProcess(delta)` |

Implementing an interface alone does not register it. Expose it using `.As<T>()` or `.AsImplementedInterfaces()`.
Participants must be singleton or instance registrations; transient participants are rejected.
Do not rely on the order of different services within a phase.

The root's processing and pause mode control dispatch. Individual participant nodes' processing flags do not filter these callbacks.
A child created during a tick joins after the current traversal. Closing it removes its participants.
Use `QueueFree()` rather than synchronously freeing the root inside its own lifecycle callback.

To add phases, override `ConfigureLifecycle` on the root and call the base method to keep physics dispatch.
Do not call `UseLifecycle()` in installers; the addon enables it on the root.
When overriding scope callbacks, call the base implementation so preparation, dispatch and cleanup still happen.

## Register from code

Instead of a resource installer, or in addition to one, override `Configure`:

```csharp
using Injectra;
using Injectra.Godot;

public partial class LevelScope : InjectraScope
{
	protected override void Configure(ContainerBuilder builder)
	{
		builder.Register<LevelState>(Lifetime.Singleton);
	}
}

public sealed class LevelState;
```

Resource installers run first, then `Configure`. You can also call plain `IInstaller` implementations there.
See [Injectra](https://github.com/UnknownProgers/Injectra) for factories, open generics, aliases and the container API.

## Ownership

| Object | Who disposes it? |
|---|---|
| Container-created singleton | Its declaring container |
| Transient service | Its consumer |
| `RegisterInstance` value | External owner by default; `.OwnedByContainer()` transfers disposal |
| Factory-created node | Caller or Godot scene tree |

Scopes close in `_ExitTree()`, after descendants leave, or when a prepared detached scope is freed.
Godot still owns native node/resource lifetimes. Do not transfer disposal to the container just to match a scope lifetime.

## Inspect the running game

1. Build the project.
2. Enable **Injectra Diagnostics** in **Project → Project Settings → Plugins**.
3. Run the game from the editor.
4. Open **Debugger → Injectra**, then press **Refresh**.
5. Select a row for details, or use **Copy text** for the container tree.

The panel shows scope/container hierarchies, registration aliases, lifetimes, ownership, overrides and constructor dependencies.
Created entries appear before pending registrations. Scope paths appear in the details panel.
Factory dependencies are unknown; transient instances are not tracked. Container IDs belong to the current snapshot.

Inspection is read-only: it does not resolve services or invoke factories. Refresh is manual, with no per-frame scanning.
Resume a paused game to let it answer a refresh request. The panel clears when the game stops.
The plugin manages an `InjectraDiagnostics` Autoload for debugger communication; disabling the plugin removes it.

## Performance

Measured inside Godot 4.6.1 .NET on Linux x64, AMD Ryzen 7 9800X3D, .NET 11,
using Injectra 0.7.0-alpha.1. Optimized editor build, tiered compilation disabled;
medians of seven short samples. These are indicative measurements, not export-build guarantees.

| Operation | Time | Managed allocation per operation |
|---|---:|---:|
| Generated method injection, warm | 43 ns | 0 B |
| Reflection method injection, warm | 68 ns | 48 B |
| Reflection method injection, new injector | 5.6–5.9 µs | 6,088 B |
| PackedScene instantiation without DI | 1.45–1.48 µs | 320 B |
| SceneFactory instantiation + generated root injection | 1.58–1.60 µs | 320 B |
| Scope creation + preparation, four user registrations | 2.48–3.17 µs | 5,576 B |

Method injection uses an existing node and three singleton arguments. A new injector
includes reflection plan discovery; it does not mean a cold process or first JIT compilation.
Scene creation uses a single-node scene without parent attachment. Scope preparation
includes node creation, container registrations and lifecycle setup, without entering the scene tree.
Cleanup is excluded. Allocation figures cover managed memory, not Godot's native allocations.

In this scene fixture, SceneFactory adds approximately 0.12–0.14 µs over direct
instantiation, with no additional managed allocation per operation. Real costs depend
on scene size, injection targets and service constructors.

## Current limits

- Main-thread use only.
- Some type/method shapes use reflection instead of generated calls, as described above.
- Other platforms and export configurations have not yet been validated.

## License

[MIT](LICENSE).

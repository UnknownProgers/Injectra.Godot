<p align="center">
  <img src="Core/addons/Injectra.Godot/icons/icon.png" alt="Injectra.Godot Logo" width="120"/>
</p>

<h1 align="center">Injectra.Godot</h1>

<p align="center">
  A lightweight C# dependency injection addon for Godot, built on Injectra.
</p>

<p align="center">
  <a href="https://github.com/UnknownProgers/Injectra.Godot/releases">
    <img src="https://img.shields.io/github/v/release/UnknownProgers/Injectra.Godot?include_prereleases&amp;label=release" alt="Release">
  </a>
</p>

[Injectra](https://github.com/UnknownProgers/Injectra) supplies the engine-independent container.
This addon connects it to Godot scenes: scopes, installers, public `[Inject]` methods, and scene creation before tree callbacks.

Currently in alpha. Tested with Godot 4.6.1 .NET, .NET 11, and C# 15.

## Features

- Project lifetime through an Autoload `InjectraProjectScope`.
- Scene and nested scopes with local registrations and dependency overrides.
- Inspector-assigned resource installers and code-based installers.
- Public method injection with generated direct calls for simple Node classes and cached reflection fallback.
- An automatically registered `SceneFactory` for each scope.
- Root-only injection by default; explicit subtree injection when needed.
- Container ownership and disposal supplied by Injectra.
- One lifecycle manager per root, with initialization, frame updates, and fixed physics updates for its child scopes.

## Installation

1. Use a Godot **.NET** project
2. Download `Injectra.Godot-<version>.zip` from [Releases](https://github.com/UnknownProgers/Injectra.Godot/releases).
3. Extract it next to `project.godot`:

   ```text
   MyGame/
   ├── project.godot
   ├── MyGame.csproj
   └── addons/Injectra.Godot/
       ├── InjectraScope.cs
       ├── InjectraProjectScope.cs
       ├── ResourceInstaller.cs
       ├── SceneFactory.cs
       ├── icons/
       └── ...
   ```

4. Add the underlying Injectra package to **your game's** project:

   ```sh
   dotnet add MyGame.csproj package Injectra --version 0.7.0-alpha.1
   dotnet build MyGame.csproj
   ```

The addon compiles with the game. No DLL copying or manual `MethodInjector` construction is required. Runtime DI does not require editor plugin activation.
Multitargeting and platform exports outside the tested Linux setup have not been validated yet.

## Quick start

Create a service in `GameSession.cs`:

```csharp
using System;

public sealed class GameSession : IDisposable
{
	public Guid Id { get; } = Guid.NewGuid();
	public void Dispose() { }
}
```

Register it in `GameInstaller.cs`:

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

Consume it in `GameController.cs`:

```csharp
using Godot;
using Injectra;
using Injectra.Godot;

public partial class GameController : Node
{
	private GameSession _session = null!;
	private SceneFactory _scenes = null!;

	[Inject]
	public void Construct(GameSession session, SceneFactory scenes)
	{
		_session = session;
		_scenes = scenes;
	}

	public override void _Ready() => GD.Print($"Session: {_session.Id}");
}
```

Build once so Godot discovers the classes. Create this scene:

```text
GameScope : InjectraScope          Injection = Tree
└── GameController                 GameController.cs
```

Add a `GameInstaller` resource to the scope's `Installers` array in the inspector. Run the scene.
The controller receives dependencies before its `_EnterTree()` and `_Ready()`.

**`Root` is the default.** It injects only the scope node, not its parent, siblings, or children.
Select `Tree` for the structure above. To inject a custom scope node itself, derive its script from `InjectraScope`.

## Project and scene lifetimes

For services that survive scene changes:

1. Create a scene whose root is `InjectraProjectScope`.
2. Assign its project-level installers in the inspector.
3. Add the scene in **Project Settings → Globals → Autoload**.
4. Place it before other Autoloads that depend on it.

Only one project scope can be active. Ordinary scopes select their parent as follows:

| Situation | Container parent |
|---|---|
| Scope created by `SceneFactory` | The factory's container |
| Scope loaded by Godot with a scope ancestor | The nearest ancestor scope's container |
| No scope ancestor | The active project scope's container |
| No project scope either | An independent root container |

An Autoload and the current scene are siblings in Godot, but their containers have a parent/child relationship:

```text
Godot scene tree                         DI containers

Root                                    Project container
├── ProjectScope [Autoload]              └── Level container
└── LevelScope                              └── Inventory container
    ├── Player
    └── InventoryScope
```

Register persistent services in project installers, level-specific services in level installers, and overrides in nested scope installers.
Ancestor injection delegates nested scope preparation to that scope's own container.

Singletons belong to the declaring container. Child overrides do not rebuild parent singletons.
Inherited transients use the requesting child's overrides. Each scope registers its own `SceneFactory`.

## Scene creation

Receive `SceneFactory` through `[Inject]`. For a scene with a root `Player : Node2D`:

```csharp
// Inside a node that received SceneFactory through injection:
var player = _scenes.Instantiate<Player>(playerScene, parent);

// Explicitly inject descendant nodes too:
var squad = _scenes.Instantiate<Node2D>(squadScene, parent,
	injection: SceneInjection.Tree);

// Without a parent, return an injected, detached scene:
var detached = _scenes.Instantiate<Player>(playerScene);
```

The caller or scene tree owns successful instances. Injection failure frees the newly instantiated subtree.
`parent` selects placement; it does not replace the factory's resolver.

A scope root creates a child of the factory's container and uses **its own inspector injection mode**.
In `Tree` mode, nested scopes are prepared before attachment. `Root` mode does not search descendants for scopes;
those prepare when Godot enters them into the tree.

Calling `PackedScene.Instantiate()` and `AddChild()` directly does not route ordinary nodes through the addon.
For creation by type, create a `SceneRegistry` resource and assign it to a scope's **SceneRegistry** property
(usually the project scope). Add `SceneRegistration` entries with a C# **Script** and its **Scene**:

```csharp
var player = _scenes.Instantiate<Player>(parent);
var detached = _scenes.Instantiate<Player>();
var squad = _scenes.Instantiate<Squad>(parent, injection: SceneInjection.Tree);
```

Mappings use the exact script type. Children inherit parent mappings and can override individual types through a local registry.
Mappings are snapshotted during scope preparation; editing the resource afterwards does not change a prepared factory.
Duplicate local keys and incomplete entries fail during preparation. Missing mappings and incompatible roots fail during creation.
The selected script must belong to a Godot C# node type. No nodes are instantiated to discover registry keys.
If several scenes share the same script, register one default and use the explicit `PackedScene` overload for alternatives.
The demo project scope registers `DemoActor`; scene and child factories inherit this mapping while using their own dependencies.

## Initialization order

### Scene loaded by Godot

```text
Scope._EnterTree()
    → select parent container
    → Installers in inspector array order
    → Configure(builder)
    → register current scope and local SceneFactory
    → Build() or parent.CreateChild(...)
    → preparation callback: inject nodes and prepare nested scopes in Tree mode
    → attach prepared lifecycle participants and initialize them
    → descendant _EnterTree() callbacks
    → descendant _Ready() callbacks
    → scope _Ready()
```

The scope's own `_EnterTree()` has already started; selected descendants receive injection before their callbacks.
Override scope callbacks with `base._EnterTree()` first, and call `base._ExitTree()` when leaving.

### Scene created by the factory

```text
PackedScene.Instantiate()
    → inject root or selected subtree
      (scope roots first build their own container)
    → attach scope lifecycle participants and initialize them
    → AddChild(parent), if supplied
    → _EnterTree() callbacks, parent before children
    → _Ready() callbacks, children before parent
```

Factory-selected nodes receive dependencies **before `_EnterTree()`**, not just before `_Ready()`.
Prepared scopes do not build or inject again when their `_EnterTree()` follows.

Creation during a lifecycle callback defers the new scope's attachment and initialization until that traversal ends.
Injection still finishes immediately; `Initialize()` always precedes the participant's first tick.

`Build()` validates registrations and prepares constructor graphs. Services are created on resolution unless marked `.NonLazy()`.
Installers configure registrations: do not spawn scenes, build the builder, or start game logic inside them.

## Injection methods

`[Inject]` belongs to `Injectra`. Supported methods are public instance synchronous `void` methods, without generic parameters,
`ref`, `out`, `in`, pointers, function pointers, or ref-struct arguments.

Each declaring class may have at most one injection method. Different inheritance levels may each declare one.
Overrides inherit `[Inject]`; repeating the attribute is allowed and does not add another call.
An override plus another injection method in the same class is still a duplicate, with or without the repeated attribute.
Duplicate methods produce compiler error `IGG001` for Node classes when the generator is present;
reflection fallback throws `InjectionException` before invoking any injection methods.
Calls follow base-to-derived slot order. A virtual override replaces its base slot and runs once at that slot's position.
`IGG002` reports non-public/static methods, non-void returns, async methods, generic injection methods,
and by-reference, pointer, ref-struct or variable-argument parameters. Signature errors point to the method or parameter.
The same validation applies to local overrides and to Node shapes that otherwise use reflection fallback.
Fields, properties, static methods, and async injection are unsupported.
Plain C# services should normally use constructor injection.

An attribute alone does not make an arbitrary node participate: a scope or factory must select it.
Injection is not a `_Ready()` callback. Child tree callbacks may not have run yet; keep tree-dependent startup in Godot callbacks.

Resources are not scanned recursively, and `RegisterInstance` does not automatically invoke injection methods.
The internal injector supports resources, but the public scope API does not expose automatic resource-injection registration yet.

## Godot lifecycle

A root scope automatically enables one `InjectraLifecycle` and initializes it after container preparation.
The Autoload project scope is normally this root. A scene launched without a project scope drives its own manager.
Child scopes inherit the manager and do not dispatch it again.

Register lifecycle contracts explicitly:

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

// In an installer:
// builder.Register<Simulation>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
```

| Contract | Source |
|---|---|
| `Injectra.Lifecycle.IInitializable` | After branch preparation, once |
| `Injectra.Lifecycle.IFrameTickable` | Root scope `_Process(delta)` |
| `Injectra.Godot.IPhysicsTickable` | Root scope `_PhysicsProcess(delta)` |

Implementing an interface is not enough: expose it through `.As<T>()` or `.AsImplementedInterfaces()`.
Participants must be singleton or instance registrations; transient lifecycle registrations are rejected.
Registered nodes must also be selected for method injection when they depend on `[Inject]` methods.

`Initialize()` does not promise a ready SceneTree. It can run while a scene is detached; inside a tick, it waits until the current
lifecycle traversal completes. Keep tree-dependent startup in Godot callbacks.
Closing a child scope stops its participants and disposes its owned services. A child created inside a tick starts participating in the next phase call.
Do not synchronously free a root scope inside its own lifecycle callback; use Godot's `QueueFree()` to close it after the callback returns.

The root's Godot processing and pause mode control dispatch. Per-participant Godot node processing flags do not filter lifecycle phases.
Override `ConfigureLifecycle` on a root scope to add custom phases, calling the base method to preserve physics dispatch.
Do not call `UseLifecycle()` again in installers: the addon registers it once on the root.

## Installers from code

Override `Configure` to invoke plain `IInstaller` implementations or add registrations directly:

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

Resource installers run before `Configure`. The fluent API, open generics, factories, and `AsImplementedInterfaces()` behave as
described in the [Injectra documentation](https://github.com/UnknownProgers/Injectra).

## Ownership and shutdown

| Object | Owner |
|---|---|
| Constructed singleton | Its declaring container |
| Transient service | Its consumer |
| `RegisterInstance` value | External by default; `.OwnedByContainer()` transfers disposal |
| Factory-created node | Caller or Godot scene tree |

Scope shutdown runs in `_ExitTree()`, after descendant exit callbacks. The container disposes owned services; Godot remains
responsible for nodes. Freeing a prepared, detached scope also closes its container.

Closing a level preserves project services. Closing the project container also disposes remaining children.
A scope that left the tree is permanently closed: create a new instance instead of re-entering it.
Do not transfer nodes or resources to container ownership simply to match scope lifetime; native Godot ownership still applies.

## Method generation

`Injectra.Godot.Generation` is a compiler-only generator, developed in this repository's `Generator` project.
The local Godot project references it as an analyzer. NuGet publication is not configured yet.

For a top-level, nongeneric partial Node class, it emits a complete inherited plan of supported injection methods.
The existing scope and scene factory automatically use that path. No manual injector registration is required.
Generated code resolves arguments through the current container, preserving child overrides and error causes.

Generated plans include inherited methods, virtual overrides, hidden methods and concrete leaves without local attributes.
Valid methods on nested/generic target classes, custom external base classes, and methods that cannot safely be called directly retain reflection fallback. Invalid local signatures produce `IGG002` instead.
An inherited generated implementation refuses an uncovered runtime type so it cannot silently skip derived injection methods.
The addon remains usable without the generator through cached reflection. This generator intentionally targets Nodes;
ordinary C# services use Injectra constructor injection and its constructor generator.

## Editor diagnostics

1. Build the C# project, then enable **Injectra Diagnostics** in **Project → Project Settings → Plugins**.
2. Run the game from the editor.
3. Open the bottom **Debugger** panel and select its **Injectra** tab.
4. Press **Refresh** to capture the running game's container hierarchy. Select a row to inspect its details.
5. Use **Copy text** to copy the Core diagnostic tree to the clipboard.

The view is read-only. It shows container children, registration aliases, created/pending state, lifetimes,
ownership, overrides, and constructor dependency sources. Created entries come first within each container.
Container rows use compact numeric labels. Their details show the current Godot scope path; plain C# containers and detached scopes have no attached scope path.
Container IDs are local to the snapshot.
Factory dependencies are unknown, and transient instances are not tracked.

The plugin automatically installs an `InjectraDiagnostics` Autoload for editor/runtime communication.
It registers a message receiver only when Godot's debugger is connected. Disabling the plugin removes the Autoload.
Snapshots are requested manually, with no per-frame scanning. No services are resolved and no factories are invoked by inspection.
When the game stops, the panel clears its snapshot and disables runtime actions. A paused game must resume to answer a refresh request.
Each editor debugging session has its own tab contents. This inspects live scopes; it does not predict containers from edited scenes.

Development and release builds use the same addon directory: `addons/Injectra.Godot`.
The repository contains one plugin configuration at `Core/addons/Injectra.Godot/plugin.cfg`.
The debugger UI is declared in `Diagnostics/DiagnosticsPanel.tscn`.

## Current boundaries

- Main-thread use only.
- Method generation covers top-level, nongeneric partial Node classes and supported local inheritance chains. Custom external bases and unsupported target shapes use reflection fallback.
- No separate addon NuGet package. The editor plugin is optional and used only for diagnostics.

## Demo and development

Open `Core/project.godot` and run `main.tscn`. The Autoload supplies `DemoProjectSession`; the scene scope supplies `DemoSession`
and `SceneFactory`. Actors display the shared project session ID and receive both sessions before `_EnterTree()`.

The dependency tree starts with two child examples. The same `DemoInstaller` registers a new local `DemoSession`;
the different `DemoChildInstaller` adds only `DemoChildState`, so that child inherits its parent's `DemoSession`.
Matching IDs identify shared instances; each scope has its own factory.
Select a scene or child branch, choose the installer mode, and create an actor or another child under that selection.
Creation from `FrameTick` captures the selected parent when queued. Closing a branch removes its descendants and disposes owned services,
while inherited sessions and unrelated branches continue ticking.

Build and run tests with the Godot .NET executable on your PATH:

```sh
dotnet build Injectra.Godot.slnx
dotnet test Tests/Generator.Tests/Generator.Tests.csproj
godot --headless --path Core --editor --import
godot --headless --path Core res://Tests/LifecycleTests.tscn
```

Engine integration tests live in `Core/Tests` and run inside the Godot project. Generator tests live in `Tests/Generator.Tests` and run with `dotnet test`.

To develop against local Injectra sources, use `-p:InjectraSourcePath=/absolute/path/to/Injectra`, or set that property in an ignored
`Directory.Build.local.props`. Releases use the published NuGet dependency.

## License

[MIT](LICENSE).

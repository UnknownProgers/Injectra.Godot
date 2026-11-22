<p align="center">
  <img src="Core/addon/icons/icon.png" alt="Injectra.Godot Logo" width="120"/>
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
- Public method injection with cached reflection metadata.
- An automatically registered `SceneFactory` for each scope.
- Root-only injection by default; explicit subtree injection when needed.
- Container ownership and disposal supplied by Injectra.

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
   dotnet add MyGame.csproj package Injectra --version 0.5.1-alpha.2
   dotnet build MyGame.csproj
   ```

The addon compiles with the game. No DLL copying, editor plugin activation, or manual `MethodInjector` construction is required.
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
Explicit `PackedScene` arguments are required; a type-to-scene registry is not implemented yet.

## Initialization order

### Scene loaded by Godot

```text
Scope._EnterTree()
    → select parent container
    → Installers in inspector array order
    → Configure(builder)
    → register current scope and local SceneFactory
    → Build() or parent.CreateChild(...)
    → inject selected nodes; prepare nested scopes in Tree mode
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
    → AddChild(parent), if supplied
    → _EnterTree() callbacks, parent before children
    → _Ready() callbacks, children before parent
```

Factory-selected nodes receive dependencies **before `_EnterTree()`**, not just before `_Ready()`.
Prepared scopes do not build or inject again when their `_EnterTree()` follows.

`Build()` validates registrations and prepares constructor graphs. Services are created on resolution unless marked `.NonLazy()`.
Installers configure registrations: do not spawn scenes, build the builder, or start game logic inside them.

## Injection methods

`[Inject]` belongs to `Injectra`. Supported methods are public instance synchronous `void` methods, without generic parameters,
`ref`, `out`, `in`, pointers, function pointers, or ref-struct arguments.

Multiple marked methods are allowed. Base methods run before derived methods; virtual overrides run once.
Method order within one declaring type is unspecified. Fields, properties, static methods, and async injection are unsupported.
Plain C# services should normally use constructor injection.

An attribute alone does not make an arbitrary node participate: a scope or factory must select it.
Injection is not a `_Ready()` callback. Child tree callbacks may not have run yet; keep tree-dependent startup in Godot callbacks.

Resources are not scanned recursively, and `RegisterInstance` does not automatically invoke injection methods.
The internal injector supports resources, but the public scope API does not expose automatic resource-injection registration yet.

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

## Current boundaries

- Main-thread use only.
- Cached metadata, but reflection-based method calls. Generated method injection is not implemented yet.
- No automatic Godot frame, physics, or `IInitializable` dispatch yet.
- No separate addon NuGet package or editor `plugin.cfg` required.

## Demo and development

Open `Core/project.godot` and run `main.tscn`. The Autoload supplies `DemoProjectSession`; the scene scope supplies `DemoSession`
and `SceneFactory`. Actors display the shared project session ID and receive both sessions before `_EnterTree()`.

Build and run tests with the Godot .NET executable on your PATH:

```sh
dotnet build Injectra.Godot.slnx
godot --headless --path Core --editor --import
godot --headless --path Core res://Tests/LifecycleTests.tscn
```

To develop against local Injectra sources, use `-p:InjectraSourcePath=/absolute/path/to/Injectra`, or set that property in an ignored
`Directory.Build.local.props`. Releases use the published NuGet dependency.

## License

[MIT](LICENSE).

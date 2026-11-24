# Injectra.Godot

A lightweight C# runtime dependency injection addon for Godot, built on [Injectra](https://github.com/UnknownProgers/Injectra).

## Installation

1. Extract the release into your project so this directory is `addons/injectra/`.
2. Add the container and method generator packages to your game, then build:

   ```sh
   dotnet add MyGame.csproj package Injectra --version 0.7.2-alpha.1
   dotnet add MyGame.csproj package Injectra.Godot.Generation --version 0.9.9-alpha.1
   dotnet build MyGame.csproj
   ```

Tested with Godot 4.6.1 .NET, .NET 11 and C# 15. Your game must target .NET 11.
Use the generator version matching the addon release; no manual analyzer or DLL references are needed.

The runtime DI features do not require editor plugin activation. These sources compile with the game.

## Usage

- Add `InjectraScope` to a scene and assign `ResourceInstaller` resources in its inspector.
- Select `Tree` to inject children; the default `Root` injects only the scope node itself.
- Receive dependencies through public instance synchronous `void` methods marked `[Inject]`.
- Receive `SceneFactory` through injection; it creates and injects scenes before `AddChild()`.
- Add an `InjectraProjectScope` scene as an Autoload for persistent services. Place it before dependent Autoloads.
- Child scopes inherit registrations, allow local overrides, and have their own factory. Without a project scope, a scene can run
  independently.
- Root scopes automatically drive one lifecycle manager. Expose `IInitializable`, `IFrameTickable` (from `Injectra.Lifecycle`), and
  `IPhysicsTickable` (from `Injectra.Godot`) to enable callbacks.

Factory injection happens before `_EnterTree()` and `_Ready()`. Godot-loaded scopes prepare in their own `_EnterTree()`, before
selected descendants' callbacks. Closing a scene scope disposes local owned services and preserves project services.
The shared order is build, method injection, initialization, then ticks. Scopes created during a tick initialize after that traversal
finishes.
`Initialize()` does not guarantee SceneTree readiness. Lifecycle participants must be singleton or instance registrations.

See the [full documentation](https://github.com/UnknownProgers/Injectra.Godot#readme) for examples, initialization order,
ownership rules, and current limitations. Distributed under the MIT license included in this directory.

## Scene registry

Assign a `InjectraSceneRegistry` resource to a scope (usually the project scope). Its `Entries` contain
`InjectraSceneRegistration` resources: select the C# node `Script` and its `Scene`.

```csharp
var player = scenes.Instantiate<Player>(parent);
```

Children inherit mappings and may override individual types with a local registry. Keys are exact C# types.
Mappings are fixed when the scope is prepared. Duplicate keys and incomplete entries are rejected.
Use `Instantiate<Player>(packedScene, parent)` when choosing between scenes sharing the same script.

## Editor diagnostics

1. Build the C# project, then enable **Injectra Diagnostics** in **Project → Project Settings → Plugins**.
2. Run the game from the editor.
3. Open the bottom **Debugger** panel and select its **Injectra** tab.
4. Press **Refresh** to capture the running game's container hierarchy. Select a row to inspect its details.
5. Use **Copy text** to copy the Core diagnostic tree to the clipboard.

The view is read-only. It shows container children, registration aliases, created/pending state, lifetimes,
ownership, overrides, and constructor dependency sources. Containers appear first; registrations are sorted by implementation name. Row labels show only the implementation; contracts are listed in the details panel.
Container rows use compact numeric labels. Their details show the current Godot scope path; plain C# containers and detached scopes have no
attached scope path.
Container IDs are local to the snapshot.

Root containers stay expanded; all branches below them start collapsed. Expand a registration to see its direct dependencies; nested dependency branches stay closed.
Shift + click on a collapse arrow expands that entire branch; Ctrl + click collapses it recursively.
Generic contracts are grouped under entries such as `ILogger<>`; expand a group to inspect its closed registrations.
Factory dependencies are unknown, and transient instances are not tracked.

The plugin automatically installs an `InjectraDiagnostics` Autoload for editor/runtime communication.
It registers a message receiver only when Godot's debugger is connected. Disabling the plugin removes the Autoload.
Snapshots are requested manually, with no per-frame scanning. No services are resolved and no factories are invoked by inspection.
When the game stops, the panel clears its snapshot and disables runtime actions. A paused game must resume to answer a refresh request.
Each editor debugging session has its own tab contents. This inspects live scopes; it does not predict containers from edited scenes.

## Method generation

Install the compiler-only `Injectra.Godot.Generation` NuGet package alongside the addon, as shown above.
It generates C# during compilation and adds no runtime library. The base `Injectra` package separately supplies constructor generation.
The generator emits complete inherited plans for top-level, nongeneric partial Node classes.
Each class may declare at most one injection method; overrides inherit `[Inject]` and may repeat it without changing calls.
An override plus another local injection method is a duplicate in either case.
Duplicate declarations produce `IGG001` at compile time, or `InjectionException` before any calls on reflection fallback.
Scopes and scene factories select generated plans automatically. Types without injection methods get empty plans, avoiding reflection
discovery.
Each generated plan accepts only its exact runtime type. Valid nested/generic targets and custom external bases retain cached reflection
fallback.

`IGG002` reports invalid injection signatures during compilation, including invalid overrides.
Methods must be public instance synchronous void methods, without method type parameters, by-reference,
ref-struct, pointer/function-pointer or variable-argument parameters. Correct unsupported target shapes retain fallback.

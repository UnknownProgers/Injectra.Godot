# Injectra.Godot

A lightweight C# runtime dependency injection addon for Godot, built on [Injectra](https://github.com/UnknownProgers/Injectra).

## Installation

1. Extract the release into your project so this directory is `addons/Injectra.Godot/`.
2. Add the underlying container package to the game and build:

   ```sh
   dotnet add MyGame.csproj package Injectra --version 0.6.0-alpha.1
   dotnet build MyGame.csproj
   ```

No editor plugin activation is required. These sources compile with the game.

## Usage

- Add `InjectraScope` to a scene and assign `ResourceInstaller` resources in its inspector.
- Select `Tree` to inject children; the default `Root` injects only the scope node itself.
- Receive dependencies through public instance synchronous `void` methods marked `[Inject]`.
- Receive `SceneFactory` through injection; it creates and injects scenes before `AddChild()`.
- Add an `InjectraProjectScope` scene as an Autoload for persistent services. Place it before dependent Autoloads.
- Child scopes inherit registrations, allow local overrides, and have their own factory. Without a project scope, a scene can run independently.
- Root scopes automatically drive one lifecycle manager. Expose `IInitializable`, `IFrameTickable` (from `Injectra.Lifecycle`), and `IPhysicsTickable` (from `Injectra.Godot`) to enable callbacks.

Factory injection happens before `_EnterTree()` and `_Ready()`. Godot-loaded scopes prepare in their own `_EnterTree()`, before
selected descendants' callbacks. Closing a scene scope disposes local owned services and preserves project services.
The shared order is build, method injection, initialization, then ticks. Scopes created during a tick initialize after that traversal finishes.
`Initialize()` does not guarantee SceneTree readiness. Lifecycle participants must be singleton or instance registrations.

See the [full documentation](https://github.com/UnknownProgers/Injectra.Godot#readme) for examples, initialization order,
ownership rules, and current limitations. Distributed under the MIT license included in this directory.

## Scene registry

Assign a `SceneRegistry` resource to a scope (usually the project scope). Its `Entries` contain
`SceneRegistration` resources: select the C# node `Script` and its `Scene`.

```csharp
var player = scenes.Instantiate<Player>(parent);
```

Children inherit mappings and may override individual types with a local registry. Keys are exact C# types.
Mappings are fixed when the scope is prepared. Duplicate keys and incomplete entries are rejected.
Use `Instantiate<Player>(packedScene, parent)` when choosing between scenes sharing the same script.

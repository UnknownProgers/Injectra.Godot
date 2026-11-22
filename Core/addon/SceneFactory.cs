using Godot;

namespace Injectra.Godot;

/// <summary>Instantiates and injects a scene before adding it to the scene tree.</summary>
public sealed class SceneFactory
{
	private readonly MethodInjector _injector;
	private readonly IResolver _resolver;

	/// <summary>Creates a factory whose dependencies are resolved from the supplied resolver.</summary>
	/// <exception cref="ArgumentNullException">The resolver is null.</exception>
	public SceneFactory(IResolver resolver)
	{
		_injector = new MethodInjector(resolver);
		_resolver = resolver;
	}

	/// <summary>Creates a scene, injects the selected nodes, and optionally adds its root to a parent.</summary>
	/// <remarks>
	/// The parent controls placement, not dependency resolution. Without a parent the returned root remains detached.
	/// The caller or scene tree owns a successful instance. A failed instance is freed immediately.
	/// By default only the root is injected, without traversing descendants. Tree mode injects the entire subtree.
	/// A scope root creates a child of the factory's container and uses its own injection setting instead of this argument.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The scene is null.</exception>
	/// <exception cref="ObjectDisposedException">The scene or parent has been freed.</exception>
	/// <exception cref="ArgumentException">The scene cannot be instantiated, or the parent is queued for deletion.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The injection mode is not supported.</exception>
	/// <exception cref="InvalidCastException">The scene root is not assignable to T.</exception>
	/// <exception cref="InjectionException">Injection fails.</exception>
	/// <exception cref="InvalidOperationException">A scope configuration is invalid or the resolver cannot provide a parent container.</exception>
	/// <exception cref="RegistrationException">A scope's container registrations are invalid.</exception>
	/// <exception cref="AggregateException">Both scope injection and cleanup fail.</exception>
	public T Instantiate<T>(PackedScene scene, Node? parent = null, SceneInjection injection = SceneInjection.Root) where T : Node
	{
		ArgumentNullException.ThrowIfNull(scene);
		if (injection is not (SceneInjection.Root or SceneInjection.Tree))
			throw new ArgumentOutOfRangeException(nameof(injection), injection, "Unsupported scene injection mode.");

		if (!GodotObject.IsInstanceValid(scene))
			throw new ObjectDisposedException(nameof(scene));

		if (!scene.CanInstantiate())
			throw new ArgumentException("The packed scene cannot be instantiated.", nameof(scene));

		ValidateParent(parent);

		var instance = scene.Instantiate();
		try
		{
			if (instance is not T typed)
				throw new InvalidCastException($"Scene {scene.ResourcePath} has root {instance.GetType()}, expected {typeof(T)}.");

			if (instance is InjectraScope scope)
			{
				if (_resolver is not Container owner)
					throw new InvalidOperationException("Creating a scope scene requires a container-backed scene factory.");
				scope.Prepare(parent, owner);
			}
			else if (injection == SceneInjection.Tree)
				_injector.InjectTree(instance);
			else
				_injector.Inject(instance);

			parent?.AddChild(instance);
			return typed;
		}
		catch
		{
			if (GodotObject.IsInstanceValid(instance))
				instance.Free();
			throw;
		}
	}

	private static void ValidateParent(Node? parent)
	{
		if (parent is null)
			return;

		if (!GodotObject.IsInstanceValid(parent))
			throw new ObjectDisposedException(nameof(parent));

		if (parent.IsQueuedForDeletion())
			throw new ArgumentException("The parent is queued for deletion.", nameof(parent));
	}
}

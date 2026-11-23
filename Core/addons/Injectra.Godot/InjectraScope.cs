using Godot;
using Godot.Collections;
using Injectra.Lifecycle;

namespace Injectra.Godot;

/// <summary>Builds a container and injects the selected nodes when entering the scene tree.</summary>
/// <remarks>
/// Preparation runs in this node's EnterTree callback, before descendant EnterTree and Ready callbacks.
/// Leaving the tree permanently closes the scope. A closed scope cannot be reused.
/// Inherits the nearest ancestor scope, then the active project scope, or builds an independent root.
/// Factory-created scopes use the factory's container as their explicit parent. A root scope drives one lifecycle for the hierarchy.
/// </remarks>
[GlobalClass]
[Icon("res://addons/Injectra.Godot/icons/scope.svg")]
public partial class InjectraScope : Node
{
	private Container? _container;
	private bool _closed;
	private InjectraLifecycle? _lifecycle;
	internal Container? PreparedContainer => _container;
	internal InjectraScope? DiagnosticsRootScope { get; private set; }
	internal Container? DiagnosticsRoot => _lifecycle is null ? null : _container;

	/// <summary>Installers executed in array order before the container is built.</summary>
	[Export]
	public Array<ResourceInstaller> Installers { get; set; } = [];

	/// <summary>Optional scene mappings. Missing types fall back to ancestor scopes.</summary>
	[Export]
	public InjectraSceneRegistry? SceneRegistry { get; set; }

	/// <summary>Selects root-only injection by default, or explicit injection of descendants.</summary>
	[Export]
	public SceneInjection Injection { get; set; } = SceneInjection.Root;

	/// <summary>Gets the prepared container owned by this scope.</summary>
	/// <exception cref="InvalidOperationException">The scope is not prepared or has already closed.</exception>
	public Container Container => _container ?? throw new InvalidOperationException("The scope is not prepared or has already closed.");

	/// <summary>Prepares registrations and injection before descendants enter the tree.</summary>
	/// <exception cref="InvalidOperationException">The scope is closed, or project scope configuration is invalid.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The injection mode is invalid.</exception>
	/// <exception cref="RegistrationException">Container registrations are invalid.</exception>
	/// <exception cref="InjectionException">A method signature, dependency, or injection method fails.</exception>
	/// <exception cref="AggregateException">Both injection and container cleanup fail.</exception>
	/// <remarks>Installer and service creation exceptions propagate. Overrides must call the base method first.</remarks>
	public override void _EnterTree() => Prepare();

	/// <summary>Dispatches frame participants through the root scope's lifecycle.</summary>
	/// <remarks>Child scopes do not dispatch again. Participant exceptions propagate. Overrides must call the base method.</remarks>
	public override void _Process(double delta) => _lifecycle?.Tick(delta);

	/// <summary>Dispatches physics participants through the root scope's lifecycle.</summary>
	/// <remarks>Participant exceptions propagate. Overrides must call the base method.</remarks>
	public override void _PhysicsProcess(double delta) => _lifecycle?.Dispatch<IPhysicsTickable, double>(delta);

	/// <summary>Closes the container after descendants leave the tree.</summary>
	/// <remarks>Exceptions from service disposal propagate. Overrides must call the base method.</remarks>
	public override void _ExitTree() => Close();

	/// <summary>Also closes a prepared scope freed before it is added to the scene tree.</summary>
	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
			Close();
	}

	/// <summary>Adds code-based registrations after resource installers and before container construction.</summary>
	/// <remarks>Override to invoke plain IInstaller implementations or register services directly.</remarks>
	protected virtual void Configure(ContainerBuilder builder)
	{
	}

	/// <summary>Configures lifecycle phases once on a root scope. Override to register additional phases.</summary>
	/// <remarks>Call the base method to retain the Godot physics phase. Do not initialize or dispose the manager here.</remarks>
	protected virtual void ConfigureLifecycle(InjectraLifecycle lifecycle) =>
		lifecycle.RegisterPhase<IPhysicsTickable, double>(static (service, delta) => service.PhysicsTick(delta));

	internal void Prepare(Node? parent = null, Container? parentContainer = null)
	{
		if (_closed)
			throw new InvalidOperationException("A closed scope cannot be prepared again.");

		if (Injection is not (SceneInjection.Root or SceneInjection.Tree))
			throw new ArgumentOutOfRangeException(nameof(Injection), Injection, "Unsupported scope injection mode.");

		if (_container is not null)
			return;

		try
		{
			if (this is InjectraProjectScope project)
			{
				for (var ancestor = parent ?? GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
					if (ancestor is InjectraScope)
						throw new InvalidOperationException("A project scope cannot be nested inside another scope.");

				project.Activate();
				parentContainer = null;
			}
			else
			{
				parentContainer ??= FindParentContainer(parent ?? GetParent());
			}

			if (parentContainer is not null)
			{
				// Scope instances are already registered; retain the actual DI root, not the node placement root.
				DiagnosticsRootScope = parentContainer.ResolveAll<InjectraScope>().SingleOrDefault()?.DiagnosticsRootScope;
				_container = parentContainer.CreateChild(builder => Install(builder, parentContainer), InjectNodes);
			}
			else
			{
				DiagnosticsRootScope = this;
				var builder = new ContainerBuilder();
				Install(builder, null);
				builder.UseLifecycle(ConfigureLifecycle);
				_container = builder.Build(InjectNodes);
				_lifecycle = _container.Resolve<InjectraLifecycle>();
				_lifecycle.Initialize();
			}
		}
		catch (Exception setupError)
		{
			try
			{
				Close();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Scope setup and container cleanup failed.", setupError, cleanupError);
			}
			throw;
		}
	}

	private void InjectNodes(Container container)
	{
		_container = container;
		var injector = new MethodInjector(container);
		if (Injection == SceneInjection.Tree)
			injector.InjectTree(this);
		else
			injector.Inject(this);
	}

	private Container? FindParentContainer(Node? parent)
	{
		for (var ancestor = parent; ancestor is not null; ancestor = ancestor.GetParent())
			if (ancestor is InjectraScope scope)
			{
				return scope.Container;
			}

		return InjectraProjectScope.Active?.Container;
	}

	private void Install(ContainerBuilder builder, Container? parentContainer)
	{
		if (SceneRegistry is not null && !IsInstanceValid(SceneRegistry))
			throw new InvalidOperationException("The scene registry has been freed.");

		var scenes = SceneRegistry?.CreateLookup();
		foreach (var installer in Installers)
		{
			if (installer is null || !IsInstanceValid(installer))
				throw new InvalidOperationException("A scope installer is null or has been freed.");

			installer.Install(builder);
		}

		Configure(builder);
		builder.RegisterInstance<InjectraScope>(this);
		if (this is InjectraProjectScope project)
			builder.RegisterInstance(project);

		builder.Register<SceneFactory>(resolver =>
		{
			var parentFactory = parentContainer?.ResolveAll<SceneFactory>().SingleOrDefault();
			return new SceneFactory(resolver, scenes, parentFactory);
		}, Lifetime.Singleton);
	}

	private void Close()
	{
		_closed = true;
		_lifecycle = null;
		DiagnosticsRootScope = null;
		if (this is InjectraProjectScope project)
			project.Deactivate();

		var container = _container;
		_container = null;
		container?.Dispose();
	}
}

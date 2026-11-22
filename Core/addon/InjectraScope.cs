using Godot;
using Godot.Collections;

namespace Injectra.Godot;

/// <summary>Builds a root container and injects the selected nodes when entering the scene tree.</summary>
/// <remarks>
/// Preparation runs in this node's EnterTree callback, before descendant EnterTree and Ready callbacks.
/// Leaving the tree permanently closes the scope. A closed scope cannot be reused.
/// Nested scopes and automatic lifecycle dispatch are not supported yet.
/// </remarks>
[GlobalClass]
[Icon("res://addon/icons/scope.svg")]
public partial class InjectraScope : Node
{
	private Container? _container;
	private bool _closed;

	/// <summary>Installers executed in array order before the container is built.</summary>
	[Export]
	public Array<ResourceInstaller> Installers { get; set; } = [];

	/// <summary>Selects root-only injection by default, or explicit injection of descendants.</summary>
	[Export]
	public SceneInjection Injection { get; set; } = SceneInjection.Root;

	/// <summary>Gets the prepared container owned by this scope.</summary>
	/// <exception cref="InvalidOperationException">The scope is not prepared or has already closed.</exception>
	public Container Container => _container ?? throw new InvalidOperationException("The scope is not prepared or has already closed.");

	/// <summary>Prepares registrations and injection before descendants enter the tree.</summary>
	/// <exception cref="InvalidOperationException">The scope is closed or nested inside another scope.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The injection mode is invalid.</exception>
	/// <exception cref="RegistrationException">Container registrations are invalid.</exception>
	/// <exception cref="InjectionException">A method signature, dependency, or injection method fails.</exception>
	/// <exception cref="AggregateException">Both injection and container cleanup fail.</exception>
	/// <remarks>Installer and service creation exceptions propagate. Overrides must call the base method first.</remarks>
	public override void _EnterTree() => Prepare();

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
	protected virtual void Configure(ContainerBuilder builder) { }

	internal void Prepare(Node? parent = null)
	{
		if (_closed)
			throw new InvalidOperationException("A closed scope cannot be prepared again.");

		if (Injection is not (SceneInjection.Root or SceneInjection.Tree))
			throw new ArgumentOutOfRangeException(nameof(Injection), Injection, "Unsupported scope injection mode.");

		for (var ancestor = parent ?? GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
			if (ancestor is InjectraScope)
				throw new InvalidOperationException("Nested scopes are not supported yet.");

		if (_container is not null)
			return;

		var builder = new ContainerBuilder();
		foreach (var installer in Installers)
		{
			if (installer is null || !IsInstanceValid(installer))
				throw new InvalidOperationException("A scope installer is null or has been freed.");

			installer.Install(builder);
		}

		Configure(builder);
		builder.RegisterInstance<InjectraScope>(this);
		builder.Register<SceneFactory>(static resolver => new SceneFactory(resolver), Lifetime.Singleton);
		_container = builder.Build();

		try
		{
			var injector = new MethodInjector(_container);
			if (Injection == SceneInjection.Tree)
				injector.InjectTree(this);
			else
				injector.Inject(this);
		}
		catch (Exception injectionError)
		{
			try { Close(); }
			catch (Exception cleanupError)
			{
				throw new AggregateException("Scope injection and container cleanup failed.", injectionError, cleanupError);
			}
			throw;
		}
	}

	private void Close()
	{
		_closed = true;
		var container = _container;
		_container = null;
		container?.Dispose();
	}
}

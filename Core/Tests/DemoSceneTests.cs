using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Injectra.Godot.Demo;

namespace Injectra.Godot.Tests;

internal static class DemoSceneTests
{
	public static void Run(Node parent)
	{
		StaticUiExistsBeforeReady();
		InspectorSettingsReachServicesAndActors(parent);
		InstallerExamplesShowOverridesAndInheritance(parent);
		ActorButtonUsesSelectedScope(parent);
		ChildButtonUsesSelectedParentAndInstaller(parent);
		TickCreationCapturesSelectedParent(parent);
		ClosingParentRemovesItsWholeBranch(parent);
		ClosingInheritingChildPreservesParentSession(parent);
	}

	private static void StaticUiExistsBeforeReady()
	{
		using var scene = GD.Load<PackedScene>("res://main.tscn");
		var scope = scene.Instantiate<InjectraScope>();
		var controller = scope.GetNode<DemoController>("DemoController");

		Check(controller.GetNode<Label>("Title").Text.Length > 0, "The scene must declare its title before Ready.");
		Check(controller.InstallerMode.ItemCount == 2, "Installer options must be serialized in the scene.");
		Check(controller.DependencyTree.Columns == 4, "The scene must declare four tree columns.");
		Check(controller.DependencyTree.ColumnTitles.Length == 4, "Column titles must be serialized in the scene.");

		scope.Free();
	}

	private static void InspectorSettingsReachServicesAndActors(Node parent)
	{
		using var demo = new DemoScene(parent);
		var sceneActor = demo.Controller.GetChildren().OfType<DemoActor>().Single();
		var childActor = demo.LocalSessionChild.GetChildren().OfType<DemoActor>().Single();
		var sceneSettings = demo.Session.Settings;
		var childSettings = demo.LocalSessionChild.Session.Settings;

		Check(sceneSettings == new DemoSettings("Game session", 120), "The game installer must load its inspector values.");
		Check(childSettings == new DemoSettings("Child session", 45), "The child installer must load its own inspector values.");
		Check(ReferenceEquals(demo.InheritingChild.Session.Settings, sceneSettings), "The inheriting child must reuse parent settings.");
		Check(ReferenceEquals(sceneActor.Settings, sceneSettings), "The scene actor and service must receive the same settings instance.");
		Check(ReferenceEquals(childActor.Settings, childSettings), "The child actor and service must receive the same settings instance.");
		Check(sceneActor.Health == 120, "The scene actor must apply inspector health before EnterTree.");
		Check(childActor.Health == 45, "The child actor must apply its own inspector health before EnterTree.");
		Check(demo.ContainsText("Game session · HP 120"), "The UI must display game installer values.");
		Check(demo.ContainsText("Child session · HP 45"), "The UI must display child installer values.");
	}

	private static void InstallerExamplesShowOverridesAndInheritance(Node parent)
	{
		using var demo = new DemoScene(parent);
		var localChild = demo.LocalSessionChild;
		var inheritingChild = demo.InheritingChild;
		var childState = inheritingChild.Container.Resolve<DemoChildState>();

		Check(!ReferenceEquals(localChild.Session, demo.Session), "The same installer must register a fresh child session.");
		Check(ReferenceEquals(inheritingChild.Session, demo.Session), "The different installer must inherit the parent session.");
		Check(childState.IsInitialized, "The different installer's local service must be initialized.");
		Check(!ReferenceEquals(localChild.Scenes, inheritingChild.Scenes), "Every scope must have a local factory.");
		Check(ReferenceEquals(localChild.Project, inheritingChild.Project), "Both children must share the project session.");
		Check(demo.Session.Spawned == 2, "Actors sharing a session must share its counter.");
	}

	private static void ActorButtonUsesSelectedScope(Node parent)
	{
		using var demo = new DemoScene(parent);

		demo.SpawnActor(demo.Scope);
		Check(demo.Session.Spawned == 3, "Selecting GameScope must create a scene actor.");

		demo.SpawnActor(demo.LocalSessionChild);
		Check(demo.LocalSessionChild.Session.Spawned == 2, "Selecting a child must create an actor in that child.");
		Check(demo.Session.Spawned == 3, "A child's local actor must not increment the parent session counter.");
	}

	private static void ChildButtonUsesSelectedParentAndInstaller(Node parent)
	{
		using var demo = new DemoScene(parent);
		var selectedParent = demo.LocalSessionChild;

		var localChild = demo.CreateChild(selectedParent, ChildInstallerKind.NewSession);
		Check(!ReferenceEquals(localChild.Session, selectedParent.Session), "The selected parent must receive a locally configured child.");

		var inheritingChild = demo.CreateChild(selectedParent, ChildInstallerKind.InheritSession);
		var localState = inheritingChild.Container.Resolve<DemoChildState>();
		Check(ReferenceEquals(inheritingChild.Session, selectedParent.Session), "Inheritance must use the selected parent, not GameScope.");
		Check(demo.ContainsText(localState.Id), "The UI must display the new child-only service.");

		var parentWithState = demo.InheritingChild;
		var descendant = demo.CreateChild(parentWithState, ChildInstallerKind.NewSession);
		var inheritedState = descendant.Container.Resolve<DemoChildState>();
		Check(ReferenceEquals(inheritedState, parentWithState.Container.Resolve<DemoChildState>()), "The selected factory must preserve its dependency context.");
	}

	private static void TickCreationCapturesSelectedParent(Node parent)
	{
		using var demo = new DemoScene(parent);
		var selectedParent = demo.LocalSessionChild;
		var originalSceneFrames = demo.Session.Frames;

		demo.QueueChild(selectedParent, ChildInstallerKind.InheritSession);
		Check(selectedParent.Session.OnNextFrame is not null, "Creation must be queued on the selected context.");
		Check(demo.Session.OnNextFrame is null, "Queuing on a child must not queue on the game session.");

		// Changing the UI selection must not redirect an already queued creation.
		demo.SelectScope(demo.InheritingChild);
		InjectraProjectScope.Active!._Process(0.01);

		var createdChild = selectedParent.GetChildren().OfType<DemoChildScope>().Single();
		var childState = createdChild.Container.Resolve<DemoChildState>();
		Check(childState.IsInitialized, "Creation during Tick must initialize after traversal.");
		Check(childState.Frames == 0, "A newly created child must wait for its first frame.");
		Check(demo.Session.Frames == originalSceneFrames + 1, "An inherited session must not receive duplicate ticks.");
	}

	private static void ClosingParentRemovesItsWholeBranch(Node parent)
	{
		using var demo = new DemoScene(parent);
		var parentScope = demo.LocalSessionChild;
		var parentSession = parentScope.Session;
		var child = demo.CreateChild(parentScope, ChildInstallerKind.InheritSession);
		var childState = child.Container.Resolve<DemoChildState>();
		var grandchild = demo.CreateChild(child, ChildInstallerKind.NewSession);
		var grandchildSession = grandchild.Session;
		var unrelatedState = demo.InheritingChild.Container.Resolve<DemoChildState>();

		demo.Close(parentScope);

		Check(parentSession.IsDisposed, "Closing a scope must dispose its local session.");
		Check(childState.IsDisposed, "Closing a parent must dispose its child's owned service.");
		Check(grandchildSession.IsDisposed, "Closing a parent must dispose its grandchild's session.");
		Check(!demo.Session.IsDisposed, "The game session must survive child closure.");
		Check(!unrelatedState.IsDisposed, "An unrelated child must remain alive.");
		Check(!demo.ContainsText(childState.Id), "Closed descendants must leave the UI tree.");
		Check(demo.ContainsText(unrelatedState.Id), "Unrelated dependencies must remain in the UI tree.");
	}

	private static void ClosingInheritingChildPreservesParentSession(Node parent)
	{
		using var demo = new DemoScene(parent);
		var child = demo.InheritingChild;
		var childState = child.Container.Resolve<DemoChildState>();

		demo.Close(child);

		Check(childState.IsDisposed, "Closing an inheriting scope must dispose its local service.");
		Check(!demo.Session.IsDisposed, "Closing an inheriting scope must preserve its parent's session.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
			throw new InvalidOperationException(message);
	}

	private enum ChildInstallerKind
	{
		NewSession,
		InheritSession
	}

	// Shared scene setup only: every scenario gets fresh scopes and its own UI selection.
	private sealed class DemoScene : IDisposable
	{
		private readonly PackedScene _scene;
		public InjectraScope Scope { get; }
		public DemoController Controller { get; }
		public DemoSession Session { get; }
		public DemoChildScope LocalSessionChild { get; }
		public DemoChildScope InheritingChild { get; }

		public DemoScene(Node parent)
		{
			_scene = GD.Load<PackedScene>("res://main.tscn");
			Scope = _scene.Instantiate<InjectraScope>();
			Controller = Scope.GetNode<DemoController>("DemoController");
			parent.AddChild(Scope);
			Session = Scope.Container.Resolve<DemoSession>();
			var children = Controller.GetChildren().OfType<DemoChildScope>().ToArray();
			Check(children.Length == 2, "The demo must start with both installer examples.");
			LocalSessionChild = children.Single(scope => scope.Installers[0] is DemoInstaller);
			InheritingChild = children.Single(scope => scope.Installers[0] is DemoChildInstaller);
		}

		public void SpawnActor(InjectraScope scope)
		{
			SelectScope(scope);
			Press("CreateActor");
		}

		public DemoChildScope CreateChild(DemoChildScope parent, ChildInstallerKind installer)
		{
			SelectScope(parent);
			Controller.InstallerMode.Select((int)installer);
			Press("CreateChildScope");
			return parent.GetChildren().OfType<DemoChildScope>().Last();
		}

		public void QueueChild(DemoChildScope parent, ChildInstallerKind installer)
		{
			SelectScope(parent);
			Controller.InstallerMode.Select((int)installer);
			Press("CreateScopeInTick");
		}

		public void Close(DemoChildScope scope)
		{
			SelectScope(scope);
			Press("CloseChildScope");
		}

		public void SelectScope(InjectraScope scope)
		{
			var prefix = $"{scope.Name} ·";
			foreach (var row in Rows(Controller.DependencyTree.GetRoot()))
			{
				if (!row.GetText(0).StartsWith(prefix, StringComparison.Ordinal))
					continue;
				row.Select(0);
				return;
			}
			throw new InvalidOperationException($"Missing UI row for {scope.Name}.");
		}

		public bool ContainsText(string text)
		{
			foreach (var row in Rows(Controller.DependencyTree.GetRoot()))
				for (var column = 0; column < Controller.DependencyTree.Columns; column++)
					if (row.GetText(column).Contains(text, StringComparison.Ordinal))
						return true;
			return false;
		}

		public void Dispose()
		{
			Scope.Free();
			_scene.Dispose();
		}

		private void Press(string buttonName)
		{
			var button = Controller.GetNode<Button>($"Toolbar/{buttonName}");
			button.EmitSignal(Button.SignalName.Pressed);
		}

		private static IEnumerable<TreeItem> Rows(TreeItem root)
		{
			yield return root;
			for (var child = root.GetFirstChild(); child is not null; child = child.GetNext())
				foreach (var descendant in Rows(child))
					yield return descendant;
		}
	}
}

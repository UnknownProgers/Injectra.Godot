namespace Injectra.Godot;

/// <summary>Receives fixed-step updates from the root scope's Godot physics callback.</summary>
public interface IPhysicsTickable
{
	/// <summary>Updates the participant with elapsed physics time in seconds.</summary>
	void PhysicsTick(double deltaTime);
}

#nullable enable

using System;

namespace Injectra.Godot;

/// <summary>Reports an invalid injection method or a failure while injecting its dependencies.</summary>
public sealed class InjectionException : Exception
{
	/// <summary>Creates an injection error with an optional underlying cause.</summary>
	public InjectionException(string message, Exception? innerException = null) : base(message, innerException)
	{
	}
}

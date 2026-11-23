using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Injectra.Diagnostics;
using VariantArray = Godot.Collections.Array;

namespace Injectra.Godot.Diagnostics;

internal static class DiagnosticsData
{
	// Wire format: [text tree, rows]. Each row: [parent index, label, state, lifetime, source, details].
	internal static VariantArray Capture(IEnumerable<(string Name, Container Container)> roots, IReadOnlyDictionary<Container, Dictionary<int, string>>? scopePaths = null)
	{
		var rows = new VariantArray();
		var text = new StringBuilder();
		foreach (var (name, container) in roots)
		{
			var snapshot = container.GetDiagnostics();
			IReadOnlyDictionary<int, string> paths = new Dictionary<int, string> { [snapshot.Id] = name };
			if (scopePaths is not null && scopePaths.TryGetValue(container, out var scopedPaths))
				paths = scopedPaths;
			text.AppendLine(name).Append(snapshot).AppendLine();
			AddContainer(snapshot, -1, paths);
		}
		return [text.ToString(), rows];

		void AddContainer(ContainerSnapshot container, int parent, IReadOnlyDictionary<int, string> paths)
		{
			var label = $"Container #{container.Id}";
			var scopePath = paths.GetValueOrDefault(container.Id);
			var row = AddRow(parent, label, "", "", "", $"Container #{container.Id}\nScope path: {scopePath ?? "none (no attached scope)"}\nParent: {container.ParentId?.ToString() ?? "none"}\nLocal entries: {container.Registrations.Count}\nChildren: {container.Children.Count}");
			foreach (var registration in container.Registrations)
			{
				var aliases = string.Join(", ", registration.Contracts.Select(TypeName));
				var implementation = TypeName(registration.ImplementationType);
				var labelText = aliases == implementation ? implementation : $"{aliases} → {implementation}";
				if (registration.IsSpecialization)
					labelText += " [cached generic]";
				var source = $"Container #{registration.DeclaringContainerId}";
				if (registration.OverriddenContracts.Count != 0)
					source += " · Override";
				var details = $"Implementation: {registration.ImplementationType.FullName}\n"
					+ $"Contracts:\n  {string.Join("\n  ", registration.Contracts.Select(type => type.FullName))}\n"
					+ $"Lifetime: {registration.Lifetime}\nCreation: {registration.Activation}\n"
					+ $"Cached instance: {registration.IsCreated}\nOwnership: {registration.Ownership}\n"
					+ $"Owner container: {registration.OwnerContainerId?.ToString() ?? "none"}\nDeclared in: {source}\n"
					+ $"Overrides: {string.Join(", ", registration.OverriddenContracts.Select(TypeName))}";
				if (registration.Activation == ActivationKind.Factory)
					details += "\nFactory dependencies are unknown.";
				if (registration.ImplementationType.IsGenericTypeDefinition)
					details += "\nDependencies are deferred until generic closure.";
				var registrationRow = AddRow(row, labelText, registration.IsCreated ? "● Created" : "○ Registered",
					registration.Lifetime.ToString(), source, details);
				AddDependencies(registration.Dependencies, registrationRow, container.Id);
			}
			foreach (var child in container.Children)
				AddContainer(child, row, paths);
		}

		void AddDependencies(IReadOnlyList<DependencySnapshot> dependencies, int parent, int containerId)
		{
			foreach (var dependency in dependencies)
			{
				var label = TypeName(dependency.ContractType);
				if (dependency.ImplementationType is { } implementation && implementation != dependency.ContractType)
					label += $" → {TypeName(implementation)}";
				var state = "Dependency";
				if (dependency.IsAmbiguous)
					state = "Ambiguous";
				else if (dependency.ImplementationType is null)
					state = "Missing";
				else if (dependency.IsReference)
					state = "Reference";
				var source = "Unregistered";
				if (dependency.DeclaringContainerId is { } sourceId)
					source = sourceId == containerId ? "Local" : $"Inherited: Container #{sourceId}";
				var details = $"Contract: {dependency.ContractType.FullName}\nImplementation: {dependency.ImplementationType?.FullName ?? "unknown"}\n"
					+ $"Source: {source}\nCreation: {dependency.Activation}\nRepeated reference: {dependency.IsReference}";
				if (dependency.Activation == ActivationKind.Factory)
					details += "\nFactory dependencies are unknown.";
				if (dependency.ImplementationType?.IsGenericTypeDefinition == true)
					details += "\nDependencies are deferred until generic closure.";
				var row = AddRow(parent, label, state, "", source, details);
				AddDependencies(dependency.Dependencies, row, containerId);
			}
		}

		int AddRow(int parent, string label, string state, string lifetime, string source, string details)
		{
			var index = rows.Count;
			rows.Add(new VariantArray { parent, label, state, lifetime, source, details });
			return index;
		}
	}

	private static string TypeName(Type type)
	{
		if (!type.IsGenericType)
			return type.Name;
		return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
	}
}

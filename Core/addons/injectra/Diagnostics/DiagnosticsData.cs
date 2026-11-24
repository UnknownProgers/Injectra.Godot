#nullable enable

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
		foreach (var (name, container) in roots.OrderBy(root => root.Name, StringComparer.OrdinalIgnoreCase))
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
			foreach (var child in container.Children.OrderBy(child => $"Container #{child.Id}", StringComparer.OrdinalIgnoreCase))
				AddContainer(child, row, paths);

			var genericGroups = container.Registrations.Where(registration => GenericContract(registration) is not null)
				.GroupBy(registration => GenericContract(registration)!).ToDictionary(group => group.Key, group => group.ToArray());
			var addedGroups = new HashSet<Type>();
			foreach (var registration in container.Registrations
				         .OrderBy(registration => TypeName(GenericContract(registration) ?? registration.ImplementationType), StringComparer.OrdinalIgnoreCase)
				         .ThenBy(registration => TypeName(registration.ImplementationType), StringComparer.OrdinalIgnoreCase))
			{
				if (GenericContract(registration) is not { } family)
				{
					AddRegistration(registration, row, container.Id);
					continue;
				}
				if (!addedGroups.Add(family))
					continue;

				var members = genericGroups[family];
				var definitions = members.Where(member => member.ImplementationType.IsGenericTypeDefinition).ToArray();
				var groupRow = definitions.Length == 1
					? AddRegistration(definitions[0], row, container.Id, TypeName(family))
					: AddRow(row, TypeName(family), "Generic group", "", "",
						$"Generic contract: {family.FullName}\nRegistrations: {members.Length}");
				foreach (var member in members
					         .Where(member => definitions.Length != 1 || !ReferenceEquals(member, definitions[0]))
					         .OrderBy(member => TypeName(member.ImplementationType), StringComparer.OrdinalIgnoreCase))
					AddRegistration(member, groupRow, container.Id);
			}
		}

		int AddRegistration(RegistrationSnapshot registration, int parent, int containerId, string? label = null)
		{
			label ??= TypeName(registration.ImplementationType);
			var source = $"Container #{registration.DeclaringContainerId}";
			if (registration.OverriddenContracts.Count != 0)
				source += " · Override";
			var details = $"Implementation: {registration.ImplementationType.FullName}\n"
				+ $"Contracts:\n  {string.Join("\n  ", registration.Contracts.Select(type => type.FullName))}\n"
				+ $"Lifetime: {registration.Lifetime}\nCreation: {registration.Activation}\n"
				+ $"Cached instance: {registration.IsCreated}\nOwnership: {registration.Ownership}\n"
				+ $"Owner container: {registration.OwnerContainerId?.ToString() ?? "none"}\nDeclared in: {source}\n"
				+ $"Overrides: {string.Join(", ", registration.OverriddenContracts.Select(TypeName))}";
			if (registration.IsSpecialization)
				details += "\nCached generic specialization.";
			if (registration.Activation == ActivationKind.Factory)
				details += "\nFactory dependencies are unknown.";
			if (registration.ImplementationType.IsGenericTypeDefinition)
				details += "\nDependencies are deferred until generic closure.";
			var row = AddRow(parent, label, registration.IsCreated ? "● Created" : "○ Registered",
				registration.Lifetime.ToString(), source, details);
			AddDependencies(registration.Dependencies, row, containerId);
			return row;
		}

		void AddDependencies(IReadOnlyList<DependencySnapshot> dependencies, int parent, int containerId)
		{
			foreach (var dependency in dependencies.OrderBy(dependency => TypeName(dependency.ImplementationType ?? dependency.ContractType), StringComparer.OrdinalIgnoreCase))
			{
				var label = TypeName(dependency.ImplementationType ?? dependency.ContractType);
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

	private static Type? GenericContract(RegistrationSnapshot registration)
	{
		var contract = registration.Contracts.Where(type => type.IsGenericType)
			.OrderByDescending(type => type.IsInterface)
			.ThenBy(TypeName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		return contract?.GetGenericTypeDefinition();
	}

	private static string TypeName(Type type)
	{
		if (!type.IsGenericType)
			return type.Name;
		var marker = type.Name.IndexOf('`');
		var name = marker < 0 ? type.Name : type.Name[..marker];
		if (type.IsGenericTypeDefinition)
			return $"{name}<{new string(',', type.GetGenericArguments().Length - 1)}>";
		return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
	}
}

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules P1 and P2: a project is packable exactly when its role says so, and a package's references are packed too.</summary>
public static class PackagingRules
{
    private const string Packable = "IsPackable";

    private static readonly string[] PackableRoles = ["public contract", "embeddable"];

    public static IEnumerable<Violation> Evaluate(RuleContext context)
    {
        var source = context.Source;
        var packable = context.Scan.Projects.Where(IsPackable).Select(project => project.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var project in context.Scan.Projects)
        {
            foreach (var violation in Settings(source, project))
            {
                yield return violation;
            }

            var definition = project.IsUnderTests ? null : source.Resolve(project.Name);
            if (!project.IsUnderTests && definition is null)
            {
                yield return source.Violate("P1", project.Name, project.Path, "has no role");
                continue;
            }

            var shouldPack = definition is not null && PackableRoles.Contains(definition.Role, StringComparer.Ordinal);
            var role = definition?.Role ?? "test";
            if (shouldPack && !packable.Contains(project.Name))
            {
                yield return source.Violate("P1", project.Name, Packable, $"the {role} role must be packable, but the project file does not set {Packable} to true");
            }
            else if (!shouldPack && packable.Contains(project.Name))
            {
                yield return source.Violate("P1", project.Name, Packable, $"the {role} role must not be packable");
            }

            if (packable.Contains(project.Name))
            {
                foreach (var reference in project.References.Where(reference => reference.ResolvedPath is not null && !packable.Contains(reference.TargetName)))
                {
                    yield return source.Violate("P2", project.Name, reference.TargetName, "a packable project references a project that is not packable");
                }
            }
        }
    }

    /// <summary>Packable means the project file itself sets the value, with no condition. The last one set wins, as in MSBuild.</summary>
    private static bool IsPackable(ScannedProject project)
    {
        var last = project.Properties.LastOrDefault(property =>
            string.Equals(property.Name, Packable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(property.DefinedIn, project.Path, StringComparison.Ordinal)
            && !property.IsConditional
            && !property.InTarget);

        return last is not null && string.Equals(last.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Violation> Settings(RuleSource source, ScannedProject project)
    {
        foreach (var property in project.Properties.Where(property => string.Equals(property.Name, Packable, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(property.DefinedIn, RepositoryScan.RootBuildProps, StringComparison.Ordinal))
            {
                if (property.IsConditional || property.InTarget || !string.Equals(property.Value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    yield return source.Violate("P1", property.DefinedIn, Packable, "the default must be false, with no condition");
                }
            }
            else if (!string.Equals(property.DefinedIn, project.Path, StringComparison.Ordinal))
            {
                yield return source.Violate("P1", property.DefinedIn, Packable, "may be set only in a project file");
            }
            else if (property.IsConditional || property.InTarget)
            {
                yield return source.Violate("P1", project.Name, Packable, "must be set with no condition and outside any target");
            }
        }
    }
}

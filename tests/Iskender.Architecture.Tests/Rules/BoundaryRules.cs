using System.Text.RegularExpressions;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules A13 to A16 and A19: what the graph alone cannot see.</summary>
public static partial class BoundaryRules
{
    private const string InternalPrefix = "Iskender.";

    private const string ExecutableRole = "executable";

    private static readonly string[] ReferenceItems = ["ProjectReference", "Reference", "PackageReference"];

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        Resolution(context).Concat(Location(context)).Concat(Identity(context)).Concat(Internals(context)).Concat(DeniedPackages(context));

    /// <summary>Names a violation's source: the project, or the file that added the entry if that is another file.</summary>
    private static string SourceOf(ScannedProject project, string definedIn) =>
        string.Equals(definedIn, project.Path, StringComparison.Ordinal) ? project.Name : definedIn;

    private static IEnumerable<Violation> Resolution(RuleContext context)
    {
        var source = context.Source;
        var approvedSdks = source.ApprovedSdks.Select(sdk => sdk.Sdk).ToHashSet(StringComparer.Ordinal);

        foreach (var project in context.Scan.Projects)
        {
            foreach (var reference in project.References)
            {
                if (reference.HasUnresolvedProperty)
                {
                    yield return source.Violate("A13", SourceOf(project, reference.DefinedIn), reference.Include, "contains an MSBuild property the scan cannot resolve");
                }
                else if (reference.ResolvedPath is null)
                {
                    yield return source.Violate("A13", SourceOf(project, reference.DefinedIn), reference.Include, "does not resolve to a project file in the repository");
                }
            }

            foreach (var import in project.Imports)
            {
                if (import.HasUnresolvedProperty)
                {
                    yield return source.Violate("A13", SourceOf(project, import.DefinedIn), import.Project, "contains an MSBuild property the scan cannot resolve");
                }
                else if (import.ResolvedPath is null)
                {
                    yield return source.Violate("A13", SourceOf(project, import.DefinedIn), import.Project, "does not resolve to a file in the repository");
                }
            }

            foreach (var sdk in project.Sdks.Where(sdk => !approvedSdks.Contains(sdk.Name)))
            {
                yield return source.Violate("A13", SourceOf(project, sdk.DefinedIn), sdk.Name, "is not an approved SDK");
            }

            foreach (var assembly in project.AssemblyReferences)
            {
                var hint = assembly.Metadata.GetValueOrDefault("HintPath", string.Empty).Replace('\\', '/');
                var includedFile = Path.GetFileName(assembly.Value.Replace('\\', '/'));
                if (IsInternal(assembly.Value) || IsInternal(includedFile) || IsInternal(Path.GetFileName(hint)))
                {
                    yield return source.Violate("A13", SourceOf(project, assembly.DefinedIn), assembly.Value, "is a direct assembly reference to an Iskender project");
                }
            }

            foreach (var package in project.PackageReferences.Where(package => IsInternal(package.Value)))
            {
                yield return source.Violate("A13", SourceOf(project, package.DefinedIn), package.Value, "is a package named like an Iskender project");
            }

            foreach (var output in project.Items.Where(item => item.IsTaskOutput && ReferenceItems.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
            {
                yield return source.Violate("A13", SourceOf(project, output.DefinedIn), output.Type, "a task output creates this reference, and the scan cannot resolve its value");
            }
        }

        var scanned = context.Scan.Projects.Select(project => project.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in context.Scan.SolutionProjects.Where(entry => !scanned.Contains(entry)))
        {
            yield return source.Violate("A13", RepositoryScan.SolutionFile, entry, "the solution lists a project the scan did not read");
        }
    }

    private static bool IsInternal(string name) =>
        name.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Violation> Location(RuleContext context)
    {
        var source = context.Source;

        foreach (var project in context.Scan.Projects)
        {
            var isUnderSource = project.Path.StartsWith("src/", StringComparison.Ordinal);
            if (!isUnderSource && !project.IsUnderTests)
            {
                yield return source.Violate("A14", project.Name, project.Path, "a project must be under src/ or tests/");
            }

            if (isUnderSource && project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            {
                yield return source.Violate("A14", project.Name, project.Path, "a project under src/ must not be named *.Tests");
            }
        }

        foreach (var group in context.Scan.Projects.GroupBy(project => project.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var project in group)
            {
                yield return source.Violate("A14", project.Name, project.Path, "another project file shares this name, ignoring case");
            }
        }
    }

    private static IEnumerable<Violation> Identity(RuleContext context)
    {
        foreach (var project in context.Scan.Projects.Where(project => !project.IsUnderTests))
        {
            foreach (var property in project.Properties.Where(property => property.Name.ToUpperInvariant() is "ASSEMBLYNAME" or "ROOTNAMESPACE" or "PACKAGEID"))
            {
                if (property.Value != project.Name && !string.Equals(property.Value, "$(MSBuildProjectName)", StringComparison.OrdinalIgnoreCase))
                {
                    yield return context.Source.Violate("A15", project.Name, property.Name, $"must equal the project name, but is '{property.Value}'");
                }
            }
        }
    }

    private static IEnumerable<Violation> Internals(RuleContext context)
    {
        var source = context.Source;
        var testProjects = context.Scan.Projects
            .Where(project => project.IsUnderTests)
            .Select(project => project.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var project in context.Scan.Projects.Where(project => !project.IsUnderTests))
        {
            var granted = project.Items.SelectMany(GrantedByItem);
            foreach (var name in granted.Where(name => !testProjects.Contains(name)))
            {
                yield return source.Violate("A16", project.Name, name, "a production assembly may grant InternalsVisibleTo only to a test project");
            }
        }

        var production = context.Scan.Projects
            .Where(project => !project.IsUnderTests)
            .Select(project => (project.Name, Directory: Path.GetDirectoryName(project.Path)!.Replace('\\', '/') + "/"))
            .ToList();

        foreach (var file in SourceScan.Files(context.Root))
        {
            var owner = production.FirstOrDefault(project => file.Path.StartsWith(project.Directory, StringComparison.Ordinal));
            if (owner.Name is null)
            {
                continue;
            }

            foreach (var line in file.Lines)
            {
                var match = InternalsAttribute().Match(line.Blanked);
                if (match.Success)
                {
                    var value = FirstString().Match(line.Original, match.Index);
                    var name = value.Success ? value.Groups[1].Value : "(unreadable)";
                    if (!testProjects.Contains(name))
                    {
                        yield return source.Violate("A16", owner.Name, name, $"{file.Path} grants InternalsVisibleTo to something other than a test project");
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GrantedByItem(ScannedItem item)
    {
        if (string.Equals(item.Type, "InternalsVisibleTo", StringComparison.OrdinalIgnoreCase))
        {
            return item.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return string.Equals(item.Type, "AssemblyAttribute", StringComparison.OrdinalIgnoreCase)
            && item.Value.Contains("InternalsVisibleTo", StringComparison.Ordinal)
                ? [item.Metadata.GetValueOrDefault("_Parameter1", "(unreadable)")]
                : [];
    }

    private static IEnumerable<Violation> DeniedPackages(RuleContext context)
    {
        var source = context.Source;

        foreach (var project in context.Scan.Projects.Where(project => !project.IsUnderTests))
        {
            var definition = source.Resolve(project.Name);
            if (definition is null)
            {
                continue;
            }

            foreach (var name in definition.Denied)
            {
                var category = source.DeniedPackages[name];

                foreach (var package in project.PackageReferences.Where(package => IsDeniedPackage(category, package.Value)))
                {
                    yield return source.Violate("A19", project.Name, package.Value, $"its role denies the {name} category");
                }

                foreach (var framework in project.FrameworkReferences.Where(framework => category.FrameworkReferences.Contains(framework.Value, StringComparer.OrdinalIgnoreCase)))
                {
                    yield return source.Violate("A19", project.Name, framework.Value, $"its role denies the {name} category");
                }

                foreach (var sdk in project.Sdks.Where(sdk => category.Sdks.Contains(sdk.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    yield return source.Violate("A19", project.Name, sdk.Name, $"its role denies the {name} category");
                }

                // An executable composes adapters, so their packages are expected in its lock file.
                if (string.Equals(definition.Role, ExecutableRole, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var package in project.LockFilePackages.Where(package => IsDeniedPackage(category, package)))
                {
                    yield return source.Violate("A19", project.Name, package, $"arrives through its lock file, and its role denies the {name} category");
                }
            }
        }
    }

    /// <summary>A prefix matches the exact package ID or the ID followed by a dot.</summary>
    private static bool IsDeniedPackage(DeniedCategory category, string package) =>
        category.Prefixes.Any(prefix =>
            string.Equals(package, prefix, StringComparison.OrdinalIgnoreCase)
            || package.StartsWith($"{prefix}.", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?<!\w)InternalsVisibleTo(?:Attribute)?\s*\(")]
    private static partial Regex InternalsAttribute();

    [GeneratedRegex("""
        "([^",]*)
        """)]
    private static partial Regex FirstString();
}

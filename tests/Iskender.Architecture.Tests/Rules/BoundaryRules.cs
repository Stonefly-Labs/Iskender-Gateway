namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rule A13: what the graph alone cannot see.</summary>
public static class BoundaryRules
{
    private const string InternalPrefix = "Iskender.";

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        Resolution(context);

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
                if (IsInternal(assembly.Value) || IsInternal(Path.GetFileName(hint)))
                {
                    yield return source.Violate("A13", SourceOf(project, assembly.DefinedIn), assembly.Value, "is a direct assembly reference to an Iskender project");
                }
            }

            foreach (var package in project.PackageReferences.Where(package => IsInternal(package.Value)))
            {
                yield return source.Violate("A13", SourceOf(project, package.DefinedIn), package.Value, "is a package named like an Iskender project");
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
}

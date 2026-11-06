namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules B1 and B2: package versions come only from central management.</summary>
public static class BuildConventionRules
{
    private const string CentralManagement = "ManagePackageVersionsCentrally";

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        CentralVersions(context).Concat(LockFiles(context));

    private static string SourceOf(ScannedProject project, string definedIn) =>
        string.Equals(definedIn, project.Path, StringComparison.Ordinal) ? project.Name : definedIn;

    private static IEnumerable<Violation> CentralVersions(RuleContext context)
    {
        var source = context.Source;

        foreach (var project in context.Scan.Projects)
        {
            foreach (var package in project.PackageReferences)
            {
                var isCentral = string.Equals(package.Type, "GlobalPackageReference", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(package.DefinedIn, RepositoryScan.RootPackagesProps, StringComparison.Ordinal);

                if (!isCentral && package.Metadata.ContainsKey("Version"))
                {
                    yield return source.Violate("B1", SourceOf(project, package.DefinedIn), package.Value, "states a package version outside central management");
                }

                if (package.Metadata.ContainsKey("VersionOverride"))
                {
                    yield return source.Violate("B1", SourceOf(project, package.DefinedIn), package.Value, "states a version override");
                }
            }

            foreach (var property in project.Properties.Where(property => string.Equals(property.Name, CentralManagement, StringComparison.OrdinalIgnoreCase)))
            {
                yield return source.Violate("B1", SourceOf(project, property.DefinedIn), CentralManagement, "a project must not set central package management");
            }
        }
    }

    private static IEnumerable<Violation> LockFiles(RuleContext context)
    {
        foreach (var project in context.Scan.Projects.Where(project => !project.HasLockFile))
        {
            yield return context.Source.Violate("B2", project.Name, "packages.lock.json", "has no committed lock file");
        }
    }
}

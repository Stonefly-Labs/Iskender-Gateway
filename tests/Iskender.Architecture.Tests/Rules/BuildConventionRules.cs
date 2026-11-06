using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules B1 to B4: every project takes its versions and protected settings from the root build files.</summary>
public static class BuildConventionRules
{
    private const string CentralManagement = "ManagePackageVersionsCentrally";

    /// <summary>Files that change settings from outside a project file, allowed only at the repository root.</summary>
    private static readonly string[] RootOnlyFiles =
    [
        RepositoryScan.RootBuildProps,
        RepositoryScan.RootBuildTargets,
        RepositoryScan.RootPackagesProps,
        ".editorconfig",
    ];

    /// <summary>Files that change settings from outside a project file, allowed nowhere.</summary>
    private static readonly string[] ForbiddenFiles = ["Directory.Build.rsp", "MSBuild.rsp", ".globalconfig"];

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        CentralVersions(context)
            .Concat(LockFiles(context))
            .Concat(RequiredSettings(context))
            .Concat(ProtectedSettings(context))
            .Concat(ConfigurationFiles(context));

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

    private static IEnumerable<Violation> RequiredSettings(RuleContext context)
    {
        var source = context.Source;

        foreach (var setting in source.ProtectedSettings.Required)
        {
            var file = context.Scan.RootFiles.FirstOrDefault(root => string.Equals(root.Name, setting.File, StringComparison.Ordinal));
            var found = file?.Properties.Where(property => string.Equals(property.Name, setting.Name, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

            if (found.Count == 0)
            {
                yield return source.Violate("B3", setting.File, setting.Name, $"must be set to {setting.Value}, but is not set");
                continue;
            }

            if (found.Count > 1)
            {
                yield return source.Violate("B3", setting.File, $"{setting.Name} (count)", "must be set exactly once");
            }

            if (found.Any(property => property.IsConditional || property.InTarget))
            {
                yield return source.Violate("B3", setting.File, $"{setting.Name} (condition)", "must be set with no condition and outside any target");
            }

            foreach (var property in found.Where(property => !string.Equals(property.Value, setting.Value, StringComparison.Ordinal)))
            {
                yield return source.Violate("B3", setting.File, setting.Name, $"must be {setting.Value}, but is '{property.Value}'");
            }
        }
    }

    private static IEnumerable<Violation> ProtectedSettings(RuleContext context)
    {
        var source = context.Source;
        var settings = source.ProtectedSettings;

        foreach (var project in context.Scan.Projects)
        {
            foreach (var property in project.Properties)
            {
                if (settings.Required.Any(setting => string.Equals(setting.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return source.Violate("B4", SourceOf(project, property.DefinedIn), property.Name, "may be set only in its root build file");
                }

                if (settings.Forbidden.Any(entry => IsForbiddenName(entry, property.Name)))
                {
                    yield return source.Violate("B4", SourceOf(project, property.DefinedIn), property.Name, "must not be set anywhere");
                }
            }

            foreach (var item in project.Items.Where(item => settings.ForbiddenItems.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
            {
                yield return source.Violate("B4", SourceOf(project, item.DefinedIn), item.Type, "this item must not appear anywhere");
            }
        }

        // The scan leaves required settings in the root props files out of each project, so one in the wrong root file is found here.
        foreach (var file in context.Scan.RootFiles)
        {
            foreach (var property in file.Properties)
            {
                var setting = settings.Required.FirstOrDefault(entry => string.Equals(entry.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                if (setting is not null && !string.Equals(setting.File, file.Name, StringComparison.Ordinal))
                {
                    yield return source.Violate("B4", file.Name, property.Name, $"may be set only in {setting.File}");
                }
            }

            foreach (var property in file.Properties.Where(property => settings.Forbidden.Any(entry => IsForbiddenName(entry, property.Name))))
            {
                yield return source.Violate("B4", file.Name, property.Name, "must not be set anywhere");
            }

            foreach (var item in file.Items.Where(item => settings.ForbiddenItems.Contains(item.Type, StringComparer.OrdinalIgnoreCase)))
            {
                yield return source.Violate("B4", file.Name, item.Type, "this item must not appear anywhere");
            }
        }
    }

    /// <summary>An entry such as <c>AnalysisMode&lt;Category&gt;</c> stands for that prefix with a category after it.</summary>
    private static bool IsForbiddenName(string entry, string name)
    {
        var placeholder = entry.IndexOf('<', StringComparison.Ordinal);
        if (placeholder < 0)
        {
            return string.Equals(entry, name, StringComparison.OrdinalIgnoreCase);
        }

        return name.Length > placeholder && name.StartsWith(entry[..placeholder], StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Violation> ConfigurationFiles(RuleContext context)
    {
        foreach (var path in RepositoryPaths.EnumerateFiles(context.Root))
        {
            var name = Path.GetFileName(path);
            var relative = RepositoryPaths.Relative(context.Root, path);

            if (ForbiddenFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                yield return context.Source.Violate("B4", relative, name, "this file must not exist anywhere");
            }
            else if (relative.Contains('/', StringComparison.Ordinal) && RootOnlyFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                yield return context.Source.Violate("B4", relative, name, "this file may exist only at the repository root");
            }
        }
    }
}

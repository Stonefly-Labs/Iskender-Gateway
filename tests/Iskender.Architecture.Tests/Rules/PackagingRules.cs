using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>The outcome of packing the solution. <c>Output</c> holds what the command printed.</summary>
public sealed record PackResult(bool Succeeded, string Output);

/// <summary>
/// Rules P1 to P3: a project is packable exactly when its role says so, and a package's references are packed too.
/// P3 needs a built solution and the real pack command, so a test calls it and <see cref="RuleRunner"/> does not.
/// </summary>
public static class PackagingRules
{
    public const string ClosureCheckVersion = "0.0.0-closure-check";

    private const string Packable = "IsPackable";

    private const string InternalPrefix = "Iskender.";

    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(5);

    private static readonly string[] PackableRoles = ["public contract", "embeddable"];

    /// <summary>The configuration the tests were built in, read from the test assembly's output path.</summary>
    public static string Configuration() =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

    /// <summary>The first part of P3: packs the solution from its existing build output.</summary>
    public static PackResult Pack(string root, string configuration, string output)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        string[] arguments =
        [
            "pack", RepositoryScan.SolutionFile, "--no-build", "-c", configuration,
            $"-p:Version={ClosureCheckVersion}", "--output", output, "-nodeReuse:false",
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(PackTimeout))
        {
            process.Kill(entireProcessTree: true);
            return new PackResult(false, $"dotnet pack did not finish within {PackTimeout}.");
        }

        var printed = $"{standardOutput.GetAwaiter().GetResult()}{standardError.GetAwaiter().GetResult()}";
        return process.ExitCode == 0
            ? new PackResult(true, printed)
            : new PackResult(false, $"dotnet pack --no-build failed with exit code {process.ExitCode}. Build the solution first, in the {configuration} configuration.\n{printed}");
    }

    /// <summary>The second part of P3: every <c>Iskender.*</c> dependency of every package in the directory is also there.</summary>
    public static IReadOnlyList<Violation> Closure(RuleSource source, string directory)
    {
        var violations = new List<Violation>();
        var packages = Directory.EnumerateFiles(directory, "*.nupkg")
            .Select(path => (Path: Path.GetFileName(path), Nuspec: ReadNuspec(path)))
            .ToList();

        if (packages.Count == 0)
        {
            violations.Add(source.Violate("P3", "pack output", "packages", "the pack produced no package"));
        }

        var produced = packages
            .Where(package => package.Nuspec is not null)
            .Select(package => ElementValue(package.Nuspec!, "id"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, nuspec) in packages)
        {
            if (nuspec is null)
            {
                violations.Add(source.Violate("P3", path, "nuspec", "the package holds no nuspec"));
                continue;
            }

            var dependencies = nuspec.Descendants()
                .Where(element => element.Name.LocalName == "dependency")
                .Select(element => element.Attribute("id")?.Value ?? string.Empty)
                .Where(id => id.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase) && !produced.Contains(id));

            violations.AddRange(dependencies.Select(id => source.Violate("P3", ElementValue(nuspec, "id"), id, "depends on a package the pack did not produce")));
        }

        return violations;
    }

    private static XDocument? ReadNuspec(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/', StringComparison.Ordinal) && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ElementValue(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;

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

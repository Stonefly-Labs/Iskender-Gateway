using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Surface;
using Iskender.Contracts.Compatibility.Tests.Wire;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>A public contract package as the run sees it: its contract ID, its version, and its current surface.</summary>
public sealed record ContractPackageInput(string Project, string Contract, int Version, IReadOnlyList<string> Surface);

/// <summary>Everything one run found: violations in catalog order, then declared breaks and deprecations.</summary>
public sealed record HarnessResult(IReadOnlyList<Violation> Violations, IReadOnlyList<Notice> Notices)
{
    public bool Passed => Violations.Count == 0;

    /// <summary>Every violation, then every notice, one per line. Printed whether the run passes or fails.</summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.Append(Passed ? "Public contract harness passed." : $"Public contract harness failed with {Violations.Count} violation(s).").Append('\n');
        foreach (var violation in Violations)
        {
            text.Append(violation.Message).Append('\n');
        }

        foreach (var notice in Notices)
        {
            text.Append(notice).Append('\n');
        }

        return text.ToString();
    }
}

/// <summary>
/// One pass over every rule family: S, V, W, G, and H1 to H7. The base is the merge base read with git, and the
/// checkout is the working tree. Packages come from the ISK-0001 rule source, by role <c>public contract</c>.
/// </summary>
public sealed partial class HarnessRun(
    IFileSet? baseFiles,
    IFileSet current,
    Func<string, ContractPackageInput?> loadPackage,
    Func<RegistrySnapshot> registries,
    Violation? baseFailure = null)
{
    public const string RuleSourcePath = "tests/Iskender.Architecture.Tests/module-dependency-rules.json";
    public const string HarnessDirectory = "tests/Iskender.Contracts.Compatibility.Tests";
    public const string BaselineDirectory = HarnessDirectory + "/baselines";
    public const string RegistryBaselinePath = BaselineDirectory + "/registries.json";
    public const string WireDirectory = HarnessDirectory + "/fixtures/wire";
    public const string RegistryContract = "core-contracts";

    private const string SurfaceSuffix = ".surface.txt";

    private readonly List<Violation> _violations = [];
    private readonly List<Notice> _notices = [];
    private readonly List<(FoundBreak Found, Violation Violation)> _breaks = [];

    /// <summary>The run on this repository: the checkout, its merge base with <c>origin/main</c>, and the referenced packages.</summary>
    public static HarnessResult OnRepository(string root, string? requestedBase = null)
    {
        var gitBase = GitBase.Resolve(root, out var failure, requestedBase);
        return new HarnessRun(gitBase, new DirectoryFileSet(root, "the checkout"), LoadReferencedPackage, RegistrySnapshot.Current, failure).Run();
    }

    /// <summary>Loads a referenced public contract assembly by project name and reads its package attribute and surface.</summary>
    public static ContractPackageInput? LoadReferencedPackage(string project)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName(project));
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }

        return assembly.GetCustomAttribute<ContractPackageAttribute>() is { } package
            ? new ContractPackageInput(project, package.ContractId, package.Version, SurfaceReader.Read(assembly))
            : null;
    }

    /// <summary>A baseline's text: one line per item, each ending in LF. A package with no public types has an empty baseline.</summary>
    public static string Baseline(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    public HarnessResult Run()
    {
        if (baseFiles is null)
        {
            return new HarnessResult(
                [baseFailure ?? new Violation("H1", "public-contracts", RuleCatalog.ContractVersion, "base", "no base", "the repository", "no base")],
                []);
        }

        // Without the rule source the package set is unknown, so no base baseline can be judged removed.
        var packages = Packages();
        if (packages is not null)
        {
            foreach (var package in packages)
            {
                CheckPackage(package);
            }

            CheckRemovedPackages(packages);
        }

        var snapshot = CheckRegistries();
        foreach (var violation in new WireChecks(baseFiles, WireDirectory, current, WireDirectory).RunAll())
        {
            if (RuleCatalog.IsBreakKind(violation.RuleId))
            {
                _breaks.Add((new FoundBreak(violation.Contract, violation.Item, violation.RuleId), violation));
            }
            else
            {
                _violations.Add(violation);
            }
        }

        var entries = BreakRecord.Parse(current.Read(RuleCatalog.BreakRecordPath));
        var record = BreakRecord.Check(
            BreakRecord.Parse(baseFiles.Read(RuleCatalog.BreakRecordPath)),
            entries,
            _breaks.Select(found => found.Found).ToList(),
            baseFiles.Name);
        _violations.AddRange(record.Violations);
        _violations.AddRange(_breaks.Where(found => !record.IsDeclared(found.Found)).Select(found => found.Violation));
        _notices.InsertRange(0, record.Declared);

        // The contract versions table needs the package set, so H4 waits for the rule source.
        if (packages is not null)
        {
            _violations.AddRange(ContractDocumentRules.Check(current, new ContractSources(snapshot, entries, packages, RuleCatalog.OwnershipResolverAllowList)));
        }

        _violations.AddRange(DeclarationRules.Check(current));

        return new HarnessResult(
            Violation.Sort(_violations),
            _notices.OrderBy(notice => notice.Kind, StringComparer.Ordinal)
                .ThenBy(notice => notice.Contract, StringComparer.Ordinal)
                .ThenBy(notice => notice.Item, StringComparer.Ordinal)
                .ToList());
    }

    private List<ContractPackageInput>? Packages()
    {
        var source = current.Read(RuleSourcePath);
        if (source is null)
        {
            Missing("rule source", $"{RuleSourcePath} is missing", RuleSourcePath);
            return null;
        }

        var projects = new List<string>();
        using (var document = JsonDocument.Parse(source))
        {
            foreach (var project in document.RootElement.GetProperty("projects").EnumerateArray())
            {
                if (project.GetProperty("role").GetString() == "public contract")
                {
                    projects.Add(project.GetProperty("name").GetString()!);
                }
            }
        }

        if (projects.Count == 0)
        {
            Missing("packages", "no project has role public contract", RuleSourcePath);
        }

        var packages = new List<ContractPackageInput>();
        foreach (var project in projects.Order(StringComparer.Ordinal))
        {
            if (loadPackage(project) is { } package)
            {
                packages.Add(package);
            }
            else
            {
                Missing($"package {project}", "the package cannot be loaded or has no [assembly: ContractPackage] attribute", RuleSourcePath);
            }
        }

        return packages;
    }

    private void CheckPackage(ContractPackageInput package)
    {
        var path = $"{BaselineDirectory}/{package.Contract}{SurfaceSuffix}";
        var compared = $"baselines/{package.Contract}{SurfaceSuffix}";
        var committed = current.ReadText(path);
        var regenerated = Baseline(package.Surface);

        if (committed is null)
        {
            _violations.Add(new Violation("H1", package.Contract, package.Version, compared, "the package has no committed baseline", compared, current.Name, regenerated));
        }
        else if (!string.Equals(committed.Replace("\r\n", "\n", StringComparison.Ordinal), regenerated, StringComparison.Ordinal))
        {
            _violations.Add(new Violation("H3", package.Contract, package.Version, compared, "the committed baseline differs from the current surface; regenerated baseline follows", compared, current.Name, regenerated));
        }

        var baseText = baseFiles!.ReadText(path);
        var baseLines = baseText is null ? null : Lines(baseText);
        var baseVersion = baseLines is null ? null : BaseVersion(package.Project);
        if (baseLines is not null && baseVersion is null)
        {
            Missing($"package {package.Project}", "the base has a baseline but no readable [assembly: ContractPackage] version", $"src/{package.Project}");
        }

        if (baseLines is not null)
        {
            var diff = SurfaceClassifier.Compare(baseLines, package.Surface);
            foreach (var change in diff.Breaks)
            {
                _breaks.Add((new FoundBreak(package.Contract, change.Item, change.RuleId), Surface(change, package.Contract, package.Version, compared)));
            }

            foreach (var deprecation in diff.Deprecations)
            {
                _notices.Add(new Notice("deprecated", deprecation.RuleId, package.Contract, deprecation.Item, deprecation.WhatChanged));
            }
        }

        foreach (var change in VersionRules.Check(package.Contract, baseLines, baseVersion, package.Surface, package.Version))
        {
            _violations.Add(Surface(change, package.Contract, package.Version, compared));
        }
    }

    /// <summary>A baseline at the base whose package is no longer a current public contract package: every type in it is S1.</summary>
    private void CheckRemovedPackages(List<ContractPackageInput> packages)
    {
        foreach (var path in baseFiles!.List(BaselineDirectory).Where(file => file.EndsWith(SurfaceSuffix, StringComparison.Ordinal)))
        {
            var contract = path[(BaselineDirectory.Length + 1)..^SurfaceSuffix.Length];
            if (packages.Any(package => package.Contract == contract))
            {
                continue;
            }

            foreach (var line in Lines(baseFiles.ReadText(path)!).Select(SurfaceLine.Parse).Where(line => line.Kind == "type"))
            {
                var change = new SurfaceChange("S1", line.Id, "type removed with its package");
                _breaks.Add((new FoundBreak(contract, change.Item, "S1"), Surface(change, contract, null, $"baselines/{contract}{SurfaceSuffix}")));
            }
        }
    }

    private RegistrySnapshot CheckRegistries()
    {
        var snapshot = registries();
        var regenerated = snapshot.ToJson();
        var committed = current.ReadText(RegistryBaselinePath);
        if (committed is null || !string.Equals(committed.Replace("\r\n", "\n", StringComparison.Ordinal), regenerated, StringComparison.Ordinal))
        {
            _violations.Add(new Violation(
                "H3", RegistryContract, null, "baselines/registries.json",
                committed is null ? "the registry baseline is missing; regenerated baseline follows" : "the committed registry baseline differs from the registries; regenerated baseline follows",
                "baselines/registries.json", current.Name, regenerated));
        }

        if (baseFiles!.Read(RegistryBaselinePath) is { } baseJson)
        {
            foreach (var change in RegistryClassifier.Compare(RegistrySnapshot.Parse(baseJson), snapshot))
            {
                _breaks.Add((new FoundBreak(RegistryContract, change.Item, change.RuleId), Surface(change, RegistryContract, null, "baselines/registries.json")));
            }
        }

        return snapshot;
    }

    /// <summary>The base's package version, from <c>[assembly: ContractPackage(..., n)]</c> in the project's sources at the base.</summary>
    private int? BaseVersion(string project)
    {
        foreach (var file in baseFiles!.List($"src/{project}").Where(file => file.EndsWith(".cs", StringComparison.Ordinal)))
        {
            if (PackageAttribute().Match(baseFiles.ReadText(file) ?? string.Empty) is { Success: true } match)
            {
                return int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private Violation Surface(SurfaceChange change, string contract, int? version, string compared) =>
        new(change.RuleId, contract, version, change.Item, change.WhatChanged, compared, baseFiles!.Name);

    private void Missing(string item, string what, string compared) =>
        _violations.Add(new Violation("H1", "public-contracts", RuleCatalog.ContractVersion, item, what, compared, current.Name));

    private static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    [GeneratedRegex(@"\[assembly:\s*ContractPackage(?:Attribute)?\(\s*""[^""]+""\s*,\s*(?<version>[0-9]+)\s*\)\]")]
    private static partial Regex PackageAttribute();
}

using System.Text.RegularExpressions;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>
/// Rules C1, C3, and C4: the contract matches the rule source, the files that hold these rules and every production
/// project have a review owner, and CI and the documentation gate know about the projects that ship.
/// </summary>
public static class ContractDocumentRules
{
    private const string NothingInternal = "(nothing internal)";

    private const string DomainAssembly = "Iskender.Domain";

    private const string ForbiddenProse = "These may not be set anywhere";

    private const string ForbiddenItemsMarker = ", and the ";

    private const string RowSeparator = " | ";

    private const string CodeOwners = ".github/CODEOWNERS";

    private const string CiWorkflow = ".github/workflows/ci.yml";

    private const string DocsGate = ".github/scripts/docs-gate.sh";

    private const string Contributing = "CONTRIBUTING.md";

    private const string GateHeading = "## Documentation merge gate";

    private const string ExecutableRole = "executable";

    private const string PublicContractRole = "public contract";

    /// <summary>A public contract since rules contract version 4, and the rule names it, since it holds the embedded request lifecycle.</summary>
    private const string HostingProject = "Iskender.Hosting";

    private static readonly char[] ShellSeparators = [' ', '\t', '|', ')'];

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        ContractMatchesSource(context).Concat(OwnerCoverage(context)).Concat(CiCoverage(context)).Concat(DocumentationCoverage(context));

    private static IEnumerable<Violation> ContractMatchesSource(RuleContext context)
    {
        var source = context.Source;
        var lines = ReadLines(context, source.ContractPath);
        if (lines is null)
        {
            yield return source.Violate("C2", source.ContractPath, "contract", "is missing");
            yield break;
        }

        var version = lines.Select(line => Regex.Match(line, @"^\*\*Version\*\*:\s*(\d+)\s*$")).FirstOrDefault(match => match.Success);
        if (version is null || !string.Equals(version.Groups[1].Value, source.ContractVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            yield return source.Violate("C1", source.ContractPath, "version", $"the contract states {version?.Groups[1].Value ?? "no version"}, but the rule source states {source.ContractVersion}");
        }

        var sections = new (string Section, IEnumerable<string> Contract, IEnumerable<string> Source)[]
        {
            ("Module map", Rows(lines, "Module map", 0, row => Join(Cell(row, 0), Cell(row, 1), Cell(row, 2))), source.Modules.Select(module => Join(module.Module, module.Assembly, module.NamespaceRoot))),
            ("Port homes", Rows(lines, "Port homes", 0, row => Join(Cell(row, 0), Cell(row, 1), Cell(row, 2))), source.PortHomes.Select(port => Join(port.Item, port.Home, port.Owner))),
            ("Project roles", Rows(lines, "Project roles", 0, row => Join(Cell(row, 0), Cell(row, 1))), ProjectRoleRows(source)),
            ("Approved direct references", Rows(lines, "Approved direct references", 0, row => Join(Cell(row, 0), SortedList(Cell(row, 1)))), source.Projects.Select(project => Join(project.Name, SortedList(project.References)))),
            ("Module namespaces", Rows(lines, $"Module namespaces in {DomainAssembly}", 0, row => Join(Cell(row, 0), SortedList(Cell(row, 1)))), ModuleNamespaceRows(source)),
            ("Required references", Rows(lines, "Required references", 0, row => Join(Cell(row, 0), SortedList(Cell(row, 1)))), source.RequiredReferences.Select(required => Join(required.Key, SortedList(required.Value)))),
            ("Forbidden reach", Rows(lines, "Forbidden reach", 0, row => Join(Cell(row, 0), SortedList(Cell(row, 1)), SortedList(Cell(row, 2)))), source.ForbiddenReach.Select(reach => Join(reach.Rule, SortedList(reach.From), SortedList(reach.To)))),
            ("Rules", Rows(lines, "Rules", 0, row => Join(Cell(row, 0), Cell(row, 1), Cell(row, 2))), source.Rules.Select(rule => Join(rule.Id, rule.Retired ? $"(retired) {rule.Statement}" : rule.Statement, rule.Source))),
            ("Denied categories", Rows(lines, "Denied packages", 0, row => Join(Cell(row, 0), SortedList(Cell(row, 1)), SortedList(Cell(row, 2)), SortedList(Cell(row, 3)))), source.DeniedPackages.Select(category => Join(category.Key, SortedList(category.Value.Prefixes), SortedList(category.Value.FrameworkReferences), SortedList(category.Value.Sdks)))),
            ("Denied by project", DeniedByProjectRows(lines), source.Projects.Where(project => project.Denied.Count > 0).Select(project => Join(project.Name, SortedList(project.Denied)))),
            ("Approved SDKs", Rows(lines, "Approved SDKs", 0, row => Join(Cell(row, 0), Cell(row, 1))), source.ApprovedSdks.Select(sdk => Join(sdk.Sdk, sdk.UsedBy))),
            ("Required settings", Rows(lines, "Protected settings", 0, row => Join(Cell(row, 0), Cell(row, 1), Cell(row, 2))), source.ProtectedSettings.Required.Select(setting => Join(setting.Name, setting.Value, setting.File))),
            ("Forbidden settings", ForbiddenList(lines, items: false), source.ProtectedSettings.Forbidden),
            ("Forbidden items", ForbiddenList(lines, items: true), source.ProtectedSettings.ForbiddenItems),
            ("Listed suppressions", Rows(lines, "Listed suppressions", 0, row => Join(Cell(row, 0), Cell(row, 1), Cell(row, 2))), source.Suppressions.Select(suppression => Join(suppression.Id, suppression.Where, suppression.Reason))),
        };

        foreach (var (section, contract, expected) in sections)
        {
            var contractRows = contract.ToHashSet(StringComparer.Ordinal);
            var sourceRows = expected.ToHashSet(StringComparer.Ordinal);

            foreach (var row in contractRows.Where(row => !sourceRows.Contains(row)))
            {
                yield return source.Violate("C1", source.ContractPath, $"{section}: {row}", "is in the contract but not in the rule source");
            }

            foreach (var row in sourceRows.Where(row => !contractRows.Contains(row)))
            {
                yield return source.Violate("C1", RuleSource.RelativePath, $"{section}: {row}", "is in the rule source but not in the contract");
            }
        }
    }

    private static IEnumerable<string> ProjectRoleRows(RuleSource source) =>
        source.Projects.Select(project => Join(project.Name, project.Role))
            .Append(Join($"Any project under {source.TestProjects.Root}/", source.TestProjects.Role));

    /// <summary>Every module homed in the domain assembly may use the shared namespace, plus what the allow-list adds.</summary>
    private static IEnumerable<string> ModuleNamespaceRows(RuleSource source) =>
        source.Modules
            .Where(module => string.Equals(module.Assembly, DomainAssembly, StringComparison.Ordinal))
            .Select(module => Join(
                module.NamespaceRoot,
                SortedList(source.ModuleNamespaces.AllowList
                    .Where(allowance => string.Equals(allowance.From, module.NamespaceRoot, StringComparison.Ordinal))
                    .Select(allowance => allowance.To)
                    .Append(source.ModuleNamespaces.Shared))));

    private static IEnumerable<string> DeniedByProjectRows(List<string> lines) =>
        Rows(lines, "Denied packages", 1, row => Join(Cell(row, 0), SortedList(Cell(row, 1))))
            .SelectMany(row =>
            {
                var cells = row.Split(RowSeparator);
                return cells[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(project => Join(project, cells[1]));
            });

    /// <summary>Reads the sentence that lists forbidden settings, then the forbidden items.</summary>
    private static List<string> ForbiddenList(List<string> lines, bool items)
    {
        var sentence = lines.Find(line => line.StartsWith(ForbiddenProse, StringComparison.Ordinal));
        if (sentence is null)
        {
            return [];
        }

        var split = sentence.IndexOf(ForbiddenItemsMarker, StringComparison.Ordinal);
        var end = sentence.IndexOf(" items.", StringComparison.Ordinal);
        if (split < 0 || end < split)
        {
            return [];
        }

        var part = items ? sentence[split..end] : sentence[..split];
        return [.. Regex.Matches(part, "`([^`]+)`").Select(match => match.Groups[1].Value)];
    }

    private static IEnumerable<string> Rows(List<string> lines, string heading, int table, Func<IReadOnlyList<string>, string> toRow)
    {
        var tables = MarkdownTable.UnderHeading(lines, heading);
        return tables is null || tables.Count <= table ? [] : tables[table].Rows.Select(toRow);
    }

    private static string Cell(IReadOnlyList<string> row, int index) =>
        index < row.Count ? MarkdownTable.Plain(row[index]) : string.Empty;

    private static string Join(params string[] cells) => string.Join(RowSeparator, cells);

    private static string SortedList(string cell) =>
        string.Equals(cell, NothingInternal, StringComparison.Ordinal)
            ? string.Empty
            : SortedList(cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string SortedList(IEnumerable<string> values) =>
        string.Join(", ", values.Order(StringComparer.Ordinal));

    private static IEnumerable<Violation> CiCoverage(RuleContext context)
    {
        var source = context.Source;
        var lines = ReadLines(context, CiWorkflow);
        if (lines is null)
        {
            yield return source.Violate("C2", CiWorkflow, "CI workflow", "is missing");
            yield break;
        }

        var attested = SubjectPaths(lines);
        var checksummed = ChecksumStep(lines);

        foreach (var name in source.Projects.Where(project => project.Role == ExecutableRole).Select(project => project.Name))
        {
            if (!attested.Any(line => line.Contains($"src/{name}/bin/${{{{ env.CONFIGURATION }}}}/net10.0/{name}.dll", StringComparison.Ordinal)))
            {
                yield return source.Violate("C4", CiWorkflow, name, "the attestation subject-path does not list this executable");
            }

            if (!checksummed.Any(line => line.Contains($"src/{name}/bin/$CONFIGURATION/net10.0/{name}.dll", StringComparison.Ordinal)))
            {
                yield return source.Violate("C4", CiWorkflow, name, "the fork-PR sha256sum step does not list this executable");
            }
        }
    }

    private static List<string> SubjectPaths(List<string> lines)
    {
        var start = lines.FindIndex(line => line.TrimStart().StartsWith("subject-path:", StringComparison.Ordinal));
        if (start < 0)
        {
            return [];
        }

        var indent = IndentOf(lines[start]);
        return lines.Skip(start + 1)
            .TakeWhile(line => line.Trim().Length == 0 || IndentOf(line) > indent)
            .Where(line => !IsComment(line))
            .ToList();
    }

    private static List<string> ChecksumStep(List<string> lines)
    {
        var start = lines.FindIndex(line => !IsComment(line) && line.Contains("sha256sum artifacts/sbom", StringComparison.Ordinal));
        var step = new List<string>();
        for (var index = start; start >= 0 && index < lines.Count; index++)
        {
            step.Add(lines[index]);
            if (!lines[index].TrimEnd().EndsWith('\\'))
            {
                break;
            }
        }

        return step;
    }

    private static IEnumerable<Violation> DocumentationCoverage(RuleContext context)
    {
        var source = context.Source;
        var behaviorBearing = source.Projects
            .Where(project => project.Role is ExecutableRole or PublicContractRole)
            .Select(project => project.Name)
            .Append(HostingProject)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var gate = ReadLines(context, DocsGate);
        if (gate is null)
        {
            yield return source.Violate("C2", DocsGate, "documentation gate", "is missing");
        }
        else
        {
            var paths = gate
                .SkipWhile(line => !line.StartsWith("is_behavior_bearing()", StringComparison.Ordinal))
                .TakeWhile(line => !string.Equals(line.TrimEnd(), "}", StringComparison.Ordinal))
                .Where(line => !IsComment(line))
                .SelectMany(line => line.Split(ShellSeparators, StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var name in behaviorBearing.Where(name => !paths.Contains($"src/{name}/*")))
            {
                yield return source.Violate("C4", DocsGate, name, "is_behavior_bearing does not list this project");
            }
        }

        var contributing = ReadLines(context, Contributing);
        if (contributing is null)
        {
            yield return source.Violate("C2", Contributing, "documentation gate", "is missing");
        }
        else
        {
            var bullets = contributing
                .SkipWhile(line => !line.StartsWith(GateHeading, StringComparison.Ordinal))
                .Skip(1)
                .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
                .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
                .ToList();

            foreach (var name in behaviorBearing.Where(name => !bullets.Any(line => line.Contains($"`src/{name}/**`", StringComparison.Ordinal))))
            {
                yield return source.Violate("C4", Contributing, name, "the documentation merge gate does not list this project");
            }
        }
    }

    private static List<string>? ReadLines(RuleContext context, string relative)
    {
        var path = Path.Combine(context.Root, relative);
        return File.Exists(path) ? [.. File.ReadLines(path)] : null;
    }

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    private static bool IsComment(string line) => line.TrimStart().StartsWith('#');

    private static IEnumerable<Violation> OwnerCoverage(RuleContext context)
    {
        var source = context.Source;
        var path = Path.Combine(context.Root, CodeOwners);
        if (!File.Exists(path))
        {
            yield return source.Violate("C2", CodeOwners, "review owners", "is missing");
            yield break;
        }

        var entries = File.ReadLines(path).Select(ParseOwnerEntry).OfType<OwnerEntry>().ToList();

        string[] files =
        [
            RuleSource.RelativePath,
            ".editorconfig",
            RepositoryScan.RootBuildProps,
            RepositoryScan.RootPackagesProps,
            RepositoryScan.RootBuildTargets,
        ];
        var projects = context.Scan.Projects
            .Where(project => project.Path.StartsWith("src/", StringComparison.Ordinal) || project.Path.StartsWith("samples/", StringComparison.Ordinal))
            .Select(project => project.Path);

        foreach (var file in files.Concat(projects))
        {
            // The last matching pattern wins, and one with no owner takes ownership away.
            var match = entries.LastOrDefault(entry => entry.Matches(file));
            if (match is null || !match.HasOwner)
            {
                yield return source.Violate("C3", CodeOwners, file, "no review owner covers this path");
            }
        }
    }

    private static OwnerEntry? ParseOwnerEntry(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return null;
        }

        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new OwnerEntry(parts[0], parts.Length > 1);
    }

    private sealed record OwnerEntry(string Pattern, bool HasOwner)
    {
        /// <summary>Applies GitHub's rules: a slash anywhere but the end anchors a pattern to the repository root.</summary>
        public bool Matches(string path)
        {
            var body = Pattern.Trim('/');
            var isAnchored = Pattern.StartsWith('/') || body.Contains('/', StringComparison.Ordinal);
            var expression = Regex.Escape(body)
                .Replace(@"\*\*", "\u0001", StringComparison.Ordinal)
                .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\u0001", ".*", StringComparison.Ordinal);

            return Regex.IsMatch(path, $"{(isAnchored ? "^" : "(^|/)")}{expression}(/|$)", RegexOptions.CultureInvariant);
        }
    }
}

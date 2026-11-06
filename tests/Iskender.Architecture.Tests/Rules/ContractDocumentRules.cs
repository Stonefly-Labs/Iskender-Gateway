using System.Text.RegularExpressions;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>
/// Rules C3 and C4: the files that hold these rules, and every production project, have a review owner, and CI and
/// the documentation gate know about the projects that ship.
/// </summary>
public static class ContractDocumentRules
{
    private const string CodeOwners = ".github/CODEOWNERS";

    private const string CiWorkflow = ".github/workflows/ci.yml";

    private const string DocsGate = ".github/scripts/docs-gate.sh";

    private const string Contributing = "CONTRIBUTING.md";

    private const string GateHeading = "## Documentation merge gate";

    private const string ExecutableRole = "executable";

    private const string PublicContractRole = "public contract";

    /// <summary>Embeddable, but the rule names it, since it holds the embedded request lifecycle.</summary>
    private const string HostingProject = "Iskender.Hosting";

    private static readonly char[] ShellSeparators = [' ', '\t', '|', ')'];

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        OwnerCoverage(context).Concat(CiCoverage(context)).Concat(DocumentationCoverage(context));

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
            .Where(project => project.Path.StartsWith("src/", StringComparison.Ordinal))
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

using System.Text.RegularExpressions;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rule C3: the files that hold these rules, and every production project, have a review owner.</summary>
public static class ContractDocumentRules
{
    private const string CodeOwners = ".github/CODEOWNERS";

    public static IEnumerable<Violation> Evaluate(RuleContext context) => OwnerCoverage(context);

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

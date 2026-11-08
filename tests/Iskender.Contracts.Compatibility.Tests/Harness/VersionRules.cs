using System.Globalization;
using System.Text.RegularExpressions;
using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>
/// The contract's V rules over one package. Surface lines carry each item's effective tag as <c>since=&lt;contract&gt;@&lt;n&gt;</c>:
/// a member's own tag, or its type's when it has none.
/// </summary>
public static partial class VersionRules
{
    /// <summary>Runs V1 to V4. A package with no baseline at the base has a null <paramref name="baseLines"/> and <paramref name="baseVersion"/>.</summary>
    public static IReadOnlyList<SurfaceChange> Check(
        string contract, IReadOnlyList<string>? baseLines, int? baseVersion, IReadOnlyList<string> current, int currentVersion)
    {
        var changed = baseLines is null || !baseLines.Order(StringComparer.Ordinal).SequenceEqual(current.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        return [.. Tags(contract, baseLines ?? [], current, currentVersion), .. Version(contract, baseVersion, currentVersion, changed)];
    }

    /// <summary>
    /// V1, V3, and V4. With <paramref name="currentVersion"/>, V1 also requires every item new since the base to be tagged
    /// with that version. A member of a new type that inherits the type's tag is covered by the type.
    /// </summary>
    public static IReadOnlyList<SurfaceChange> Tags(string contract, IEnumerable<string> baseLines, IEnumerable<string> currentLines, int? currentVersion = null)
    {
        var changes = new List<SurfaceChange>();
        var current = currentLines.Select(SurfaceLine.Parse).ToList();
        var was = baseLines.Select(SurfaceLine.Parse).ToDictionary(line => line.Id, StringComparer.Ordinal);
        var types = current.Where(line => line.Kind == "type").ToDictionary(line => line.Key, StringComparer.Ordinal);
        var untagged = new HashSet<string>(StringComparer.Ordinal);
        var expected = currentVersion is { } version ? $"{contract}@{Text(version)}" : null;

        foreach (var line in current.Where(line => line.Kind == "type"))
        {
            if (!Names(line.Get("since"), contract))
            {
                untagged.Add(line.Key);
                changes.Add(new("V1", line.Id, MissingTag(line.Get("since"), contract)));
            }
            else if (expected is not null && !was.ContainsKey(line.Id) && line.Get("since") != expected)
            {
                changes.Add(new("V1", line.Id, NotCurrent(line.Get("since")!, expected)));
            }
        }

        foreach (var line in current.Where(line => line.Kind != "type"))
        {
            var since = line.Get("since");
            var typeSince = types.TryGetValue(line.DeclaringType!, out var type) ? type.Get("since") : null;

            // A member of an untagged type that has no tag of its own is covered by its type's V1.
            if (since is null && untagged.Contains(line.DeclaringType!))
            {
                continue;
            }

            if (!Names(since, contract))
            {
                changes.Add(new("V1", line.Id, MissingTag(since, contract)));
            }
            else if (!was.ContainsKey(line.Id) && was.ContainsKey($"type {line.DeclaringType}") && since == typeSince)
            {
                changes.Add(new("V1", line.Id, "member added after its type has no tag of its own"));
            }
            else if (expected is not null && !was.ContainsKey(line.Id) && since != expected && !(since == typeSince && !was.ContainsKey($"type {line.DeclaringType}")))
            {
                changes.Add(new("V1", line.Id, NotCurrent(since!, expected)));
            }
        }

        foreach (var line in current)
        {
            if (was.TryGetValue(line.Id, out var before) && before.Get("since") is { } old && old != line.Get("since"))
            {
                changes.Add(new("V3", line.Id, $"tag changed from {old} to {line.Get("since") ?? "none"}"));
            }

            if (line.Get("obsolete") is { } message && !IsDeprecation(message, contract))
            {
                changes.Add(new("V4", line.Id, $"deprecation message \"{message}\" does not state its version and replacement"));
            }
        }

        return changes;
    }

    /// <summary>V2: an unchanged surface keeps the base version, a changed one is the base plus one, and a first baseline starts at 1.</summary>
    public static IReadOnlyList<SurfaceChange> Version(string contract, int? baseVersion, int currentVersion, bool surfaceChanged)
    {
        var expected = baseVersion is not { } version ? 1 : surfaceChanged ? version + 1 : version;
        if (currentVersion == expected)
        {
            return [];
        }

        var reason = baseVersion is null ? "the package has no baseline at the base" : surfaceChanged ? "the surface changed" : "the surface is unchanged";
        return [new("V2", $"package {contract}", $"version is {Text(currentVersion)}, expected {Text(expected)}: {reason}")];
    }

    private static bool Names(string? since, string contract) =>
        since is not null && since.StartsWith(contract + "@", StringComparison.Ordinal) && int.TryParse(since[(contract.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static string MissingTag(string? since, string contract) =>
        since is null ? $"no version tag naming {contract}" : $"version tag {since} does not name {contract}";

    private static string NotCurrent(string since, string expected) => $"new item tagged {since}, expected {expected}, the package's current version";

    private static bool IsDeprecation(string message, string contract) =>
        Deprecation().Match(message) is { Success: true } match && match.Groups["contract"].Value == contract;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^Deprecated in (?<contract>[a-z0-9-]+)@[1-9][0-9]*\. (Use \S.*\.|No replacement\.)$")]
    private static partial Regex Deprecation();
}

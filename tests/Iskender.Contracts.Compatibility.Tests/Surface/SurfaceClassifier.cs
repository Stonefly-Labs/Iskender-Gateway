namespace Iskender.Contracts.Compatibility.Tests.Surface;

/// <summary>One classified difference: a break with its S rule ID, or a compatible change.</summary>
public sealed record SurfaceChange(string RuleId, string Item, string WhatChanged);

/// <summary>Everything that changed between two surfaces.</summary>
public sealed record SurfaceDiff(
    IReadOnlyList<SurfaceChange> Breaks,
    IReadOnlyList<string> Additions,
    IReadOnlyList<SurfaceChange> Deprecations);

/// <summary>
/// Classifies the differences between a base surface and a current one with the contract's Public .NET surface table
/// (S1 to S9). Additions are compatible, and a newly obsolete item is a deprecation.
/// </summary>
public static class SurfaceClassifier
{
    public static SurfaceDiff Compare(IEnumerable<string> baseLines, IEnumerable<string> currentLines)
    {
        var before = baseLines.Select(SurfaceLine.Parse).ToDictionary(line => line.Id, StringComparer.Ordinal);
        var after = currentLines.Select(SurfaceLine.Parse).ToDictionary(line => line.Id, StringComparer.Ordinal);
        var breaks = new List<SurfaceChange>();
        var additions = new List<string>();
        var deprecations = new List<SurfaceChange>();

        foreach (var (id, line) in before)
        {
            if (after.TryGetValue(id, out var current))
            {
                if (!line.Equals(current))
                {
                    Changed(line, current, breaks, deprecations);
                }

                continue;
            }

            if (line.Kind == "type")
            {
                breaks.Add(new("S1", id, "type removed, renamed, or moved"));
            }
            else if (!after.ContainsKey("type " + line.DeclaringType))
            {
                // Its type was removed, which S1 reports.
            }
            else if (line.Kind == "enum-value")
            {
                breaks.Add(new("S8", id, "enum value removed or renamed"));
            }
            else
            {
                breaks.Add(new("S2", id, "member removed, renamed, or its parameters changed"));
            }
        }

        foreach (var (id, line) in after)
        {
            if (before.ContainsKey(id))
            {
                continue;
            }

            if (line.Kind != "type" && line.Has("abstract") && before.TryGetValue("type " + line.DeclaringType, out var owner))
            {
                var ownerKind = owner.Get("kind");
                if (ownerKind == "interface" || (ownerKind == "class" && owner.Has("abstract")))
                {
                    breaks.Add(new("S7", id, $"abstract member added to a public {(ownerKind == "interface" ? "interface" : "abstract class")}"));
                    continue;
                }
            }

            additions.Add(id);
        }

        return new SurfaceDiff(breaks, additions, deprecations);
    }

    private static void Changed(SurfaceLine before, SurfaceLine after, List<SurfaceChange> breaks, List<SurfaceChange> deprecations)
    {
        void Break(string rule, string what) => breaks.Add(new(rule, before.Id, what));

        if (before.Get("obsolete") is null && after.Get("obsolete") is { } message)
        {
            deprecations.Add(new("V4", before.Id, message));
        }

        if (Narrowed(before.Get("access"), after.Get("access")))
        {
            Break("S6", "accessibility narrowed");
        }

        if (before.Kind == "type")
        {
            if (before.Get("kind") != after.Get("kind"))
            {
                Break("S9", $"kind changed from {before.Get("kind")} to {after.Get("kind")}");
            }

            if (before.Get("base") is { } baseType && baseType != after.Get("base"))
            {
                Break("S9", $"base type {baseType} removed or changed");
            }

            var lost = Split(before.Get("interfaces")).Except(Split(after.Get("interfaces")), StringComparer.Ordinal).ToList();
            if (lost.Count > 0)
            {
                Break("S9", $"interface removed: {string.Join(",", lost)}");
            }

            foreach (var flag in new[] { "sealed", "abstract", "static" })
            {
                if (!before.Has(flag) && after.Has(flag))
                {
                    Break("S6", $"type became {flag}");
                }
            }

            if (before.Get("underlying") != after.Get("underlying"))
            {
                Break("S8", "enum underlying type changed");
            }

            return;
        }

        if (before.Kind == "enum-value")
        {
            if (before.Get("value") != after.Get("value"))
            {
                Break("S8", $"enum value renumbered from {before.Get("value")} to {after.Get("value")}");
            }

            return;
        }

        if (!before.Has("abstract") && after.Has("abstract"))
        {
            Break("S6", "member became abstract");
        }

        if ((before.Has("virtual") || before.Has("abstract")) && !after.Has("virtual") && !after.Has("abstract"))
        {
            Break("S6", "member is no longer overridable");
        }

        if (before.Has("static") != after.Has("static"))
        {
            Break("S6", before.Has("static") ? "member is no longer static" : "member became static");
        }

        if (!before.Has("readonly") && after.Has("readonly"))
        {
            Break("S6", "field became readonly");
        }

        if (before.Has("const") != after.Has("const") || before.Get("value") != after.Get("value"))
        {
            Break("S4", "constant changed");
        }

        foreach (var name in new[] { "returns", "type" })
        {
            CompareType(before.Get(name), after.Get(name), name == "returns" ? "return type" : "type", Break);
        }

        foreach (var accessor in new[] { "get", "set", "init" })
        {
            var was = before.Get(accessor);
            var now = after.Get(accessor);
            if (was is not null && (now is null || Narrowed(was, now)))
            {
                Break("S6", now is null ? $"{accessor} accessor removed or changed" : $"{accessor} accessor narrowed");
            }
        }

        CompareParameters(before.Get("params"), after.Get("params"), Break);
    }

    private static void CompareType(string? before, string? after, string what, Action<string, string> report)
    {
        if (before is null || before == after)
        {
            return;
        }

        if (after is null || Strip(before) != Strip(after))
        {
            report("S4", $"{what} changed from {before} to {after}");
        }
        else
        {
            report("S5", $"{what} nullable annotation changed from {before} to {after}");
        }
    }

    private static void CompareParameters(string? before, string? after, Action<string, string> report)
    {
        var was = Split(before);
        var now = Split(after);
        for (var index = 0; index < Math.Min(was.Count, now.Count); index++)
        {
            var (wasPassing, wasName, wasType, wasDefault) = Parameter(was[index]);
            var (nowPassing, nowName, nowType, nowDefault) = Parameter(now[index]);

            if (wasName != nowName)
            {
                report("S3", $"parameter {wasName} renamed to {nowName}");
            }

            if (wasPassing != nowPassing)
            {
                report("S4", $"parameter {wasName} passing changed");
            }

            if (wasType != nowType)
            {
                report(Strip(wasType) == Strip(nowType) ? "S5" : "S4", $"parameter {wasName} annotation changed from {wasType} to {nowType}");
            }

            if (wasDefault is not null && wasDefault != nowDefault)
            {
                report("S4", $"parameter {wasName} default removed or changed");
            }
        }
    }

    /// <summary>Splits <c>[out |ref |in ]name[marker | :type][=default]</c>.</summary>
    private static (string Passing, string Name, string Annotation, string? Default) Parameter(string text)
    {
        var passing = string.Empty;
        foreach (var prefix in new[] { "out ", "ref ", "in " })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                passing = prefix.Trim();
                text = text[prefix.Length..];
            }
        }

        string? defaultValue = null;
        var equals = TopLevelIndex(text, '=');
        if (equals >= 0)
        {
            defaultValue = text[(equals + 1)..];
            text = text[..equals];
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            return (passing, text[..colon], text[(colon + 1)..], defaultValue);
        }

        var end = text.Length > 0 && text[^1] is '!' or '?' or '~' ? text.Length - 1 : text.Length;
        return (passing, text[..end], text[end..], defaultValue);
    }

    private static int TopLevelIndex(string text, char target)
    {
        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            depth += text[index] switch { '<' => 1, '>' => -1, _ => 0 };
            if (depth == 0 && text[index] == target)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Splits a comma list at depth zero, so generic arguments stay together.</summary>
    internal static List<string> Split(string? text)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return parts;
        }

        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..index]);
                    start = index + 1;
                    break;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static string Strip(string text) => new(text.Where(character => character is not ('!' or '?' or '~')).ToArray());

    private static bool Narrowed(string? before, string? after) => before == "public" && after == "protected";
}

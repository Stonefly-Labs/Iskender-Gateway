namespace Iskender.Domain.Registry;

/// <summary>
/// Suggests a declared name for an unknown one: the closest sibling within an optimal string alignment distance of 2,
/// ignoring ASCII case, with ties going to the ordinal-first name; otherwise up to 10 siblings in ordinal order (FR-019).
/// </summary>
internal static class NameSuggester
{
    private const int MaxDistance = 2;
    private const int MaxListed = 10;

    /// <summary>The closest sibling, or null with the siblings to list instead.</summary>
    public static (string? Closest, IReadOnlyList<string> Listed) Suggest(string name, IEnumerable<string> siblings)
    {
        var ordered = siblings.Order(StringComparer.Ordinal).ToList();
        string? closest = null;
        var best = MaxDistance + 1;
        foreach (var sibling in ordered)
        {
            var distance = Distance(name, sibling);
            if (distance < best)
            {
                best = distance;
                closest = sibling;
            }
        }

        return closest is not null ? (closest, []) : (null, ordered.Take(MaxListed).ToList());
    }

    /// <summary>Optimal string alignment distance: Levenshtein plus adjacent transposition, folding ASCII case.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = AsciiIgnoreCaseComparer.Fold(a[i - 1]) == AsciiIgnoreCaseComparer.Fold(b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && AsciiIgnoreCaseComparer.Fold(a[i - 1]) == AsciiIgnoreCaseComparer.Fold(b[j - 2])
                    && AsciiIgnoreCaseComparer.Fold(a[i - 2]) == AsciiIgnoreCaseComparer.Fold(b[j - 1]))
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}

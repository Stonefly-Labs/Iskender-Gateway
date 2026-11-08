using System.Text.RegularExpressions;

namespace Iskender.Contracts.Compatibility.Tests.Support;

/// <summary>One Markdown table: its header cells and its rows, with no separator row.</summary>
public sealed partial record MarkdownTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>
    /// Returns every table under a heading, in order, including tables in its subsections, or null if the heading is missing.
    /// A heading matches after backticks are removed. The section ends at the next heading of the same or a higher level.
    /// A table indented inside a list item counts.
    /// </summary>
    public static IReadOnlyList<MarkdownTable>? UnderHeading(IEnumerable<string> lines, string heading)
    {
        var tables = new List<MarkdownTable>();
        var inside = false;
        var level = 0;
        List<IReadOnlyList<string>>? current = null;

        void Finish()
        {
            if (current is { Count: > 0 })
            {
                tables.Add(new MarkdownTable(current[0], current.Skip(1).ToList()));
            }

            current = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var match = Heading().Match(line);
            if (match.Success)
            {
                Finish();
                if (inside && match.Groups[1].Length <= level)
                {
                    return tables;
                }

                if (!inside && string.Equals(Plain(match.Groups[2].Value), heading, StringComparison.Ordinal))
                {
                    inside = true;
                    level = match.Groups[1].Length;
                }

                continue;
            }

            var trimmed = line.TrimStart();
            if (!inside)
            {
                continue;
            }

            if (!trimmed.StartsWith('|'))
            {
                Finish();
                continue;
            }

            var cells = trimmed.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
            if (!cells.All(cell => cell.All(character => character is '-' or ':' or ' ')))
            {
                current ??= [];
                current.Add(cells);
            }
        }

        Finish();
        return inside ? tables : null;
    }

    /// <summary>A cell without its backticks.</summary>
    public static string Plain(string cell) => cell.Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    [GeneratedRegex(@"^(#+)\s+(.*?)\s*$")]
    private static partial Regex Heading();
}

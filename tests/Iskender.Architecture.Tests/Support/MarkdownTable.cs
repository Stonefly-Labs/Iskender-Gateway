using System.Text.RegularExpressions;

namespace Iskender.Architecture.Tests.Support;

/// <summary>One Markdown table: its header cells and its rows, with no separator row.</summary>
public sealed record MarkdownTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>
    /// Returns every table under a heading, in order, or null if the heading is missing. A heading matches
    /// after backticks are removed. The section ends at the next heading of the same or a higher level.
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

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^(#+)\s+(.*?)\s*$");
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

            if (!inside)
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                Finish();
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
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
}

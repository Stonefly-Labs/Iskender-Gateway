using System.Text;
using System.Text.RegularExpressions;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>One line of a source file: as written, and with one-line strings and <c>//</c> comments blanked out.</summary>
public sealed record SourceLine(string Original, string Blanked);

public sealed record SourceFile(string Path, string Text, IReadOnlyList<SourceLine> Lines);

/// <summary>A <c>using</c> alias of an attribute a source scan looks for. <c>Attribute</c> has no suffix or qualifier.</summary>
public sealed record AttributeAlias(string Alias, string Attribute);

/// <summary>
/// The text scan behind B5, B6, and the attribute form of A16. Blanking keeps column positions, so a value found
/// in a blanked line can be read back from the original line. A multi-line raw string is not blanked out.
/// </summary>
public static partial class SourceScan
{
    public const string InternalsVisibleTo = "InternalsVisibleTo";

    /// <summary>
    /// Every alias in a file of an attribute these scans look for. An alias hides each later use from a text match,
    /// so it is reported itself. A directive can span lines, so this reads the whole blanked text.
    /// </summary>
    public static IEnumerable<AttributeAlias> AttributeAliases(SourceFile file) =>
        AttributeAliasDirective().Matches(string.Join('\n', file.Lines.Select(line => line.Blanked)))
            .Select(match => new AttributeAlias(match.Groups[1].Value, match.Groups[2].Value));

    public static IEnumerable<SourceFile> Files(string root)
    {
        foreach (var directory in new[] { "src", "tests" })
        {
            foreach (var path in RepositoryPaths.EnumerateFiles(Path.Combine(root, directory)).Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var text = File.ReadAllText(path);
                var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).Select(line => new SourceLine(line, Blank(line))).ToList();
                yield return new SourceFile(RepositoryPaths.Relative(root, path), text, lines);
            }
        }
    }

    public static string Blank(string line)
    {
        var result = new StringBuilder(line);
        var index = 0;

        void Clear(int from, int to)
        {
            for (var position = from; position < to; position++)
            {
                result[position] = ' ';
            }
        }

        while (index < line.Length)
        {
            var character = line[index];
            if (character == '/' && index + 1 < line.Length && line[index + 1] == '/')
            {
                Clear(index, line.Length);
                break;
            }

            if (character == '\'')
            {
                index = SkipCharacterLiteral(line, index);
                continue;
            }

            if (character != '"')
            {
                index++;
                continue;
            }

            var end = EndOfString(line, index);
            if (end < 0)
            {
                // A string that does not close on this line, such as a multi-line raw string, stays as written.
                break;
            }

            Clear(index, end);
            index = end;
        }

        return result.ToString();
    }

    private static int SkipCharacterLiteral(string line, int index)
    {
        if (index + 2 < line.Length && line[index + 1] != '\\' && line[index + 2] == '\'')
        {
            return index + 3;
        }

        return index + 3 < line.Length && line[index + 1] == '\\' && line[index + 3] == '\'' ? index + 4 : index + 1;
    }

    /// <summary>The index just past the string opening at <paramref name="start"/>, or -1 if it does not close.</summary>
    private static int EndOfString(string line, int start)
    {
        var quotes = 0;
        while (start + quotes < line.Length && line[start + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var close = line.IndexOf(new string('"', quotes), start + quotes, StringComparison.Ordinal);
            return close < 0 ? -1 : close + quotes;
        }

        var verbatim = IsVerbatim(line, start);
        for (var index = start + 1; index < line.Length; index++)
        {
            if (!verbatim && line[index] == '\\')
            {
                index++;
            }
            else if (line[index] == '"')
            {
                if (verbatim && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    return index + 1;
                }
            }
        }

        return -1;
    }

    private static bool IsVerbatim(string line, int start) =>
        (start > 0 && line[start - 1] == '@') || (start > 1 && line[start - 1] == '$' && line[start - 2] == '@');

    [GeneratedRegex(@"(?<!\w)using\s+(?:unsafe\s+)?(\w+)\s*=\s*(?:[\w.:\s]*?[.:]\s*)?(InternalsVisibleTo|SuppressMessage|UnconditionalSuppressMessage|GeneratedCode)(?:Attribute)?\s*;")]
    private static partial Regex AttributeAliasDirective();
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>One line of a source file: as written, and with comments and string literals blanked out.</summary>
public sealed record SourceLine(string Original, string Blanked);

/// <summary>A string literal in a source file. <c>Value</c> is null if it has interpolation holes.</summary>
public sealed record StringLiteral(int Start, int End, string? Value);

/// <summary>
/// A source file. <c>Blanked</c> is <c>Text</c> with comments and string literals replaced by spaces, keeping every
/// position and line break, so a match in it can be read back from the original.
/// </summary>
public sealed record SourceFile(string Path, string Text, string Blanked, IReadOnlyList<SourceLine> Lines, IReadOnlyList<StringLiteral> Strings);

/// <summary>A <c>using</c> alias of an attribute a source scan looks for. <c>Attribute</c> has no suffix or qualifier.</summary>
public sealed record AttributeAlias(string Alias, string Attribute);

/// <summary>A use of an attribute a source scan looks for, with its leading string arguments, each null if it is not a plain string literal.</summary>
public sealed record AttributeUse(string Attribute, IReadOnlyList<string?> Arguments);

/// <summary>
/// The text scan behind B5, B6, and the attribute form of A16. It lexes the whole file, so a block comment, a
/// multi-line string, or an attribute split across lines reads as the compiler reads it. Code inside an
/// interpolation hole stays in place. Attribute names are matched after decoding Unicode escapes and removing
/// formatting characters, which the compiler also ignores in identifiers.
/// </summary>
public static partial class SourceScan
{
    public const string InternalsVisibleTo = "InternalsVisibleTo";

    private static readonly string[] SuppressionAttributes = ["SuppressMessage", "UnconditionalSuppressMessage", "GeneratedCode"];

    /// <summary>
    /// Every alias in a file of an attribute these scans look for. An alias hides each later use from a text match,
    /// so it is reported itself.
    /// </summary>
    public static IEnumerable<AttributeAlias> AttributeAliases(SourceFile file) =>
        AttributeAliasDirective().Matches(Identifiers.Decode(file.Blanked).Text)
            .Select(match => new AttributeAlias(match.Groups[1].Value, match.Groups[2].Value));

    /// <summary>Every use of <c>InternalsVisibleToAttribute</c>, with its first argument.</summary>
    public static IEnumerable<AttributeUse> InternalsVisibleToUses(SourceFile file) => Uses(file, InternalsAttribute(), 1);

    /// <summary>Every use of a suppression attribute, with its first two arguments.</summary>
    public static IEnumerable<AttributeUse> SuppressionUses(SourceFile file) => Uses(file, SuppressionAttribute(), 2);

    /// <summary>
    /// The suppression attribute a type name in a build item names, with no suffix or qualifier, or null. It matches
    /// with or without the <c>Attribute</c> suffix, a namespace, or <c>global::</c>.
    /// </summary>
    public static string? SuppressionAttributeName(string typeName) => AttributeName(typeName, SuppressionAttributes);

    /// <summary>Whether a type name in a build item names <c>InternalsVisibleToAttribute</c>, matched as above.</summary>
    public static bool NamesInternalsVisibleTo(string typeName) => AttributeName(typeName, [InternalsVisibleTo]) is not null;

    public static IEnumerable<SourceFile> Files(string root)
    {
        foreach (var directory in new[] { "src", "tests" })
        {
            foreach (var path in RepositoryPaths.EnumerateFiles(Path.Combine(root, directory)).Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                yield return Read(RepositoryPaths.Relative(root, path), File.ReadAllText(path));
            }
        }
    }

    public static SourceFile Read(string path, string text)
    {
        var (blanked, strings) = new Lexer(text).Run();
        var lines = text.Split('\n').Zip(blanked.Split('\n'), (original, line) => new SourceLine(original.TrimEnd('\r'), line.TrimEnd('\r'))).ToList();
        return new SourceFile(path, text, blanked, lines, strings);
    }

    public static string Blank(string text) => new Lexer(text).Run().Blanked;

    private static IEnumerable<AttributeUse> Uses(SourceFile file, Regex pattern, int count)
    {
        var decoded = Identifiers.Decode(file.Blanked);
        foreach (Match match in pattern.Matches(decoded.Text))
        {
            var end = match.Index + match.Length;
            var arguments = decoded.Text[end - 1] == '(' ? StringArguments(file, decoded.Original(end - 1) + 1, count) : [];
            yield return new AttributeUse(match.Groups[1].Value, [.. arguments, .. Enumerable.Repeat<string?>(null, count - arguments.Count)]);
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> arguments that start at <paramref name="index"/>, each a plain string
    /// literal followed by a comma or a closing parenthesis. It stops at the first argument that is anything else.
    /// </summary>
    private static List<string?> StringArguments(SourceFile file, int index, int count)
    {
        var arguments = new List<string?>();
        while (arguments.Count < count)
        {
            index = SkipWhiteSpace(file, index);
            var literal = file.Strings.FirstOrDefault(literal => literal.Start == index);
            var after = literal is null ? -1 : SkipWhiteSpace(file, literal.End);
            if (literal?.Value is null || after >= file.Blanked.Length || file.Blanked[after] is not (',' or ')'))
            {
                break;
            }

            arguments.Add(literal.Value);
            if (file.Blanked[after] == ')')
            {
                break;
            }

            index = after + 1;
        }

        return arguments;
    }

    /// <summary>Skips white space and comments, which are blank, but stops where a string literal starts, since it is blank too.</summary>
    private static int SkipWhiteSpace(SourceFile file, int index)
    {
        while (index < file.Blanked.Length && char.IsWhiteSpace(file.Blanked[index]) && !file.Strings.Any(literal => literal.Start == index))
        {
            index++;
        }

        return index;
    }

    private static string? AttributeName(string typeName, string[] candidates)
    {
        var name = typeName.Trim();
        var separator = Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':'));
        name = name[(separator + 1)..];
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name[..^"Attribute".Length];
        }

        return candidates.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?<!\w)using\s+(?:unsafe\s+)?(\w+)\s*=\s*(?:[\w.:\s]*?[.:]\s*)?(InternalsVisibleTo|SuppressMessage|UnconditionalSuppressMessage|GeneratedCode)(?:Attribute)?\s*;")]
    private static partial Regex AttributeAliasDirective();

    [GeneratedRegex(@"(?<!\w)(InternalsVisibleTo)(?:Attribute)?\s*\(")]
    private static partial Regex InternalsAttribute();

    [GeneratedRegex(@"(?<!\w)(SuppressMessage|UnconditionalSuppressMessage|GeneratedCode)(?:Attribute)?\s*(?:\(|\]|,|\z)")]
    private static partial Regex SuppressionAttribute();

    /// <summary>
    /// The blanked text with <c>\u</c> and <c>\U</c> escapes decoded and formatting characters removed, as the
    /// compiler compares identifiers, with a map from each position back to the original.
    /// </summary>
    private sealed partial class Identifiers
    {
        private readonly List<int> positions = [];

        private Identifiers(string text, List<int> positions)
        {
            Text = text;
            this.positions = positions;
        }

        public string Text { get; }

        public static Identifiers Decode(string blanked)
        {
            var text = new StringBuilder(blanked.Length);
            var positions = new List<int>(blanked.Length);
            var index = 0;
            while (index < blanked.Length)
            {
                var start = index;
                var escape = blanked[index] == '\\' ? Escape().Match(blanked, index) : Match.Empty;
                string value;
                if (escape.Success && escape.Index == index)
                {
                    value = FromCodePoint(int.Parse(escape.Groups[1].Value + escape.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += escape.Length;
                }
                else
                {
                    value = blanked[index].ToString();
                    index++;
                }

                foreach (var character in value.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format))
                {
                    text.Append(character);
                    positions.Add(start);
                }
            }

            return new Identifiers(text.ToString(), positions);
        }

        public int Original(int index) => positions[index];

        [GeneratedRegex(@"\G\\(?:u([0-9A-Fa-f]{4})|U00(0[0-9A-Fa-f]{5}|10[0-9A-Fa-f]{4}))")]
        private static partial Regex Escape();
    }

    /// <summary>A code point as text. A lone surrogate, which a char literal may hold, stays a single char.</summary>
    private static string FromCodePoint(int value) =>
        value <= char.MaxValue ? ((char)value).ToString() : char.ConvertFromUtf32(value);

    /// <summary>Blanks comments and string literals in C# text, recording each literal and its value.</summary>
    private sealed class Lexer(string text)
    {
        private readonly char[] blanked = text.ToCharArray();

        private readonly List<StringLiteral> strings = [];

        /// <summary>Positions inside interpolation holes, which stay as lexed when their literal is blanked.</summary>
        private readonly HashSet<int> kept = [];

        public (string Blanked, IReadOnlyList<StringLiteral> Strings) Run()
        {
            Code(0, insideHole: false);
            return (new string(blanked), strings);
        }

        /// <summary>Lexes code. Inside an interpolation hole, it returns the index of the brace that closes it.</summary>
        private int Code(int index, bool insideHole)
        {
            var depth = 0;
            while (index < text.Length)
            {
                var character = text[index];
                if (character == '/' && At(index + 1, '/'))
                {
                    index = Clear(index, EndOfLine(index));
                }
                else if (character == '/' && At(index + 1, '*'))
                {
                    var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    index = Clear(index, close < 0 ? text.Length : close + 2);
                }
                else if (character == '#' && !insideHole && StartsLine(index))
                {
                    index = Directive(index);
                }
                else if (character == '\'')
                {
                    index = SkipCharacterLiteral(index);
                }
                else if (character is '"' or '$' or '@' && StringStart(index) is { } start)
                {
                    index = StringLiteral(index, start.Quote, start.Dollars, start.Verbatim);
                }
                else
                {
                    if (insideHole && character == '{')
                    {
                        depth++;
                    }
                    else if (insideHole && character == '}' && depth-- == 0)
                    {
                        return index;
                    }

                    index++;
                }
            }

            return index;
        }

        /// <summary>A directive's text is not code, so it is left as written, apart from a trailing <c>//</c> comment.</summary>
        private int Directive(int index)
        {
            var end = EndOfLine(index);
            var comment = text.IndexOf("//", index, end - index, StringComparison.Ordinal);
            if (comment >= 0)
            {
                Clear(comment, end);
            }

            return end;
        }

        private (int Quote, int Dollars, bool Verbatim)? StringStart(int index)
        {
            var dollars = 0;
            var verbatim = false;
            while (index < text.Length && (text[index] == '$' || (text[index] == '@' && !verbatim)))
            {
                dollars += text[index] == '$' ? 1 : 0;
                verbatim |= text[index] == '@';
                index++;
            }

            return At(index, '"') ? (index, dollars, verbatim) : null;
        }

        private int StringLiteral(int start, int quote, int dollars, bool verbatim)
        {
            var quotes = 0;
            while (At(quote + quotes, '"'))
            {
                quotes++;
            }

            int end;
            var hasHoles = false;
            string content;
            // A verbatim string is never raw: after @, each "" is an escaped quote.
            if (quotes >= 3 && !verbatim)
            {
                (end, hasHoles) = Raw(quote + quotes, quotes, dollars);
                content = RawValue(text[(quote + quotes)..Math.Max(quote + quotes, end - quotes)]);
            }
            else if (quotes == 2 && !verbatim)
            {
                end = quote + 2;
                content = string.Empty;
            }
            else
            {
                (end, hasHoles) = Quoted(quote + 1, dollars > 0, verbatim);
                var body = text[(quote + 1)..Math.Max(quote + 1, end - 1)];
                content = verbatim ? body.Replace("\"\"", "\"", StringComparison.Ordinal) : Unescape(body);
            }

            ClearLiteral(start, end);
            strings.Add(new StringLiteral(start, end, hasHoles ? null : content));
            return end;
        }

        private (int End, bool HasHoles) Raw(int index, int quotes, int dollars)
        {
            var hasHoles = false;
            var delimiter = new string('"', quotes);
            while (index < text.Length)
            {
                if (string.CompareOrdinal(text, index, delimiter, 0, quotes) == 0)
                {
                    return (index + quotes, hasHoles);
                }

                var braces = 0;
                while (dollars > 0 && At(index + braces, '{'))
                {
                    braces++;
                }

                if (dollars > 0 && braces >= dollars)
                {
                    hasHoles = true;
                    index = Hole(index + braces) + dollars;
                }
                else
                {
                    index += Math.Max(braces, 1);
                }
            }

            return (text.Length, hasHoles);
        }

        private (int End, bool HasHoles) Quoted(int index, bool interpolated, bool verbatim)
        {
            var hasHoles = false;
            while (index < text.Length)
            {
                var character = text[index];
                if (!verbatim && character == '\n')
                {
                    return (index, hasHoles);
                }

                if (!verbatim && character == '\\')
                {
                    index += 2;
                }
                else if (character == '"' && verbatim && At(index + 1, '"'))
                {
                    index += 2;
                }
                else if (character == '"')
                {
                    return (index + 1, hasHoles);
                }
                else if (interpolated && character is '{' or '}' && At(index + 1, character))
                {
                    index += 2;
                }
                else if (interpolated && character == '{')
                {
                    hasHoles = true;
                    index = Hole(index + 1) + 1;
                }
                else
                {
                    index++;
                }
            }

            return (text.Length, hasHoles);
        }

        /// <summary>Lexes the code in an interpolation hole, and keeps it, returning the index of its closing brace.</summary>
        private int Hole(int index)
        {
            var close = Code(index, insideHole: true);
            Keep(index, close);
            return close;
        }

        private static string RawValue(string body)
        {
            if (!body.Contains('\n', StringComparison.Ordinal))
            {
                return body;
            }

            var lines = body.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var indentation = lines[^1];
            return string.Join('\n', lines.Skip(1).SkipLast(1).Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line));
        }

        private static string Unescape(string body)
        {
            var result = new StringBuilder(body.Length);
            for (var index = 0; index < body.Length; index++)
            {
                if (body[index] != '\\' || index + 1 >= body.Length)
                {
                    result.Append(body[index]);
                    continue;
                }

                var code = body[++index];
                var digits = code switch
                {
                    'u' => 4,
                    'U' => 8,
                    'x' => HexLength(body, index + 1),
                    _ => 0,
                };

                if (digits > 0
                    && index + digits < body.Length
                    && int.TryParse(body.AsSpan(index + 1, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                    && value <= 0x10FFFF)
                {
                    result.Append(FromCodePoint(value));
                    index += digits;
                }
                else
                {
                    result.Append(code switch
                    {
                        '0' => '\0',
                        'a' => '\a',
                        'b' => '\b',
                        'e' => '\u001b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        'v' => '\v',
                        _ => code,
                    });
                }
            }

            return result.ToString();
        }

        private static int HexLength(string body, int index)
        {
            var length = 0;
            while (length < 4 && index + length < body.Length && Uri.IsHexDigit(body[index + length]))
            {
                length++;
            }

            return length;
        }

        private int SkipCharacterLiteral(int index)
        {
            var end = index + 1;
            if (At(end, '\\'))
            {
                end += 2;
            }

            while (end < text.Length && end < index + 12 && text[end] is not ('\'' or '\n'))
            {
                end++;
            }

            return At(end, '\'') ? end + 1 : index + 1;
        }

        private bool StartsLine(int index)
        {
            while (index > 0 && text[index - 1] is ' ' or '\t')
            {
                index--;
            }

            return index == 0 || text[index - 1] == '\n';
        }

        private bool At(int index, char character) => index < text.Length && text[index] == character;

        private int EndOfLine(int index)
        {
            var end = text.IndexOf('\n', index);
            return end < 0 ? text.Length : end;
        }

        /// <summary>Blanks a range, keeping line breaks, and returns its end.</summary>
        private int Clear(int from, int to)
        {
            for (var position = from; position < to; position++)
            {
                if (text[position] is not ('\n' or '\r'))
                {
                    blanked[position] = ' ';
                }
            }

            return to;
        }

        /// <summary>Blanks a literal, apart from its interpolation holes, which <see cref="Keep"/> recorded as they were lexed.</summary>
        private void ClearLiteral(int from, int to)
        {
            for (var position = from; position < to; position++)
            {
                if (!kept.Contains(position) && text[position] is not ('\n' or '\r'))
                {
                    blanked[position] = ' ';
                }
            }
        }

        private void Keep(int from, int to)
        {
            for (var position = from; position < to; position++)
            {
                kept.Add(position);
            }
        }
    }
}

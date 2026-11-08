using System.Text;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// The characters a string field allows: ASCII classes plus listed punctuation, with an optional separate class for the
/// first character. There is no regular expression, so no input can make matching slow (research R7). A pattern is
/// checked when the registry is built, not when it is created.
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class CharacterClassPattern
{
    public CharacterClassPattern(CharacterClasses classes, string? punctuation = null, CharacterClasses? firstCharacter = null)
    {
        Classes = classes;
        Punctuation = punctuation ?? string.Empty;
        FirstCharacter = firstCharacter;
    }

    /// <summary>The classes every character may come from.</summary>
    public CharacterClasses Classes { get; }

    /// <summary>The punctuation characters allowed besides <see cref="Classes"/>: printable ASCII that is not a letter or digit.</summary>
    public string Punctuation { get; }

    /// <summary>The classes the first character must come from, or null when it follows the same rule as the rest.</summary>
    public CharacterClasses? FirstCharacter { get; }

    /// <summary>True when every character of <paramref name="value"/> is allowed. An empty value matches.</summary>
    public bool IsMatch(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (index == 0 && FirstCharacter is { } first)
            {
                if (!InClasses(c, first))
                {
                    return false;
                }
            }
            else if (!InClasses(c, Classes) && Punctuation.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Describes the pattern for error text, such as <c>a-z, 0-9, and "-", starting with a-z</c>.</summary>
    public override string ToString()
    {
        var parts = Describe(Classes);
        if (Punctuation.Length > 0)
        {
            parts.Add($"\"{Punctuation}\"");
        }

        var text = new StringBuilder(Join(parts));
        if (FirstCharacter is { } first)
        {
            text.Append(", starting with ").Append(Join(Describe(first)));
        }

        return text.ToString();
    }

    private static bool InClasses(char c, CharacterClasses classes) =>
        (classes.HasFlag(CharacterClasses.LowerCaseLetters) && c is >= 'a' and <= 'z')
            || (classes.HasFlag(CharacterClasses.UpperCaseLetters) && c is >= 'A' and <= 'Z')
            || (classes.HasFlag(CharacterClasses.Digits) && c is >= '0' and <= '9');

    private static List<string> Describe(CharacterClasses classes)
    {
        var parts = new List<string>();
        if (classes.HasFlag(CharacterClasses.LowerCaseLetters))
        {
            parts.Add("a-z");
        }

        if (classes.HasFlag(CharacterClasses.UpperCaseLetters))
        {
            parts.Add("A-Z");
        }

        if (classes.HasFlag(CharacterClasses.Digits))
        {
            parts.Add("0-9");
        }

        return parts;
    }

    private static string Join(List<string> parts) => parts.Count switch
    {
        0 => "no characters",
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts[..^1]) + ", and " + parts[^1],
    };
}

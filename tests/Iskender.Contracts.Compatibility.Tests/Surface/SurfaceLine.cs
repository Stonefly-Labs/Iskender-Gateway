using System.Text;

namespace Iskender.Contracts.Compatibility.Tests.Surface;

/// <summary>
/// One canonical surface line: <c>&lt;kind&gt; &lt;key&gt; | &lt;attribute&gt;=&lt;value&gt;; ...</c>. A flag attribute, such as
/// <c>static</c>, has no value. A <c>;</c> or <c>\</c> inside a value is escaped with <c>\</c>.
/// </summary>
public sealed record SurfaceLine(string Kind, string Key, IReadOnlyList<KeyValuePair<string, string?>> Attributes)
{
    /// <summary>The item's identity across versions: its kind and key.</summary>
    public string Id => $"{Kind} {Key}";

    /// <summary>For a member, the declaring type's key: the text before <c>::</c>.</summary>
    public string? DeclaringType => Kind == "type" ? null : Key[..Key.IndexOf("::", StringComparison.Ordinal)];

    public bool Has(string flag) => Attributes.Any(attribute => attribute.Key == flag && attribute.Value is null);

    public string? Get(string name) => Attributes.FirstOrDefault(attribute => attribute.Key == name && attribute.Value is not null).Value;

    public override string ToString()
    {
        var text = new StringBuilder().Append(Kind).Append(' ').Append(Key);
        if (Attributes.Count > 0)
        {
            text.Append(" | ").AppendJoin("; ", Attributes.Select(attribute => attribute.Value is null ? attribute.Key : $"{attribute.Key}={Escape(attribute.Value)}"));
        }

        return text.ToString();
    }

    public static SurfaceLine Parse(string line)
    {
        var bar = line.IndexOf(" | ", StringComparison.Ordinal);
        var head = bar < 0 ? line : line[..bar];
        var space = head.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            throw new FormatException($"Not a surface line: {line}");
        }

        var attributes = new List<KeyValuePair<string, string?>>();
        if (bar >= 0)
        {
            foreach (var part in SplitAttributes(line[(bar + 3)..]))
            {
                var equals = part.IndexOf('=', StringComparison.Ordinal);
                attributes.Add(equals < 0 ? new(part, null) : new(part[..equals], Unescape(part[(equals + 1)..])));
            }
        }

        return new SurfaceLine(head[..space], head[(space + 1)..], attributes);
    }

    public bool Equals(SurfaceLine? other) => other is not null && string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                index++;
                text.Append(value[index] == 'n' ? '\n' : value[index]);
            }
            else
            {
                text.Append(value[index]);
            }
        }

        return text.ToString();
    }

    private static IEnumerable<string> SplitAttributes(string text)
    {
        var current = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                current.Append(text[index]).Append(text[index + 1]);
                index++;
            }
            else if (text[index] == ';' && index + 1 < text.Length && text[index + 1] == ' ')
            {
                yield return current.ToString();
                current.Clear();
                index++;
            }
            else
            {
                current.Append(text[index]);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

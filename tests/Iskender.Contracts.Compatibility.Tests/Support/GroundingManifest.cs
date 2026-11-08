using Iskender.Contracts.Compatibility.Tests.Harness;

namespace Iskender.Contracts.Compatibility.Tests.Support;

/// <summary>
/// Reads <c>.iskender/grounding.yaml</c> line by line with no YAML package, tracking the section each key sits in, so
/// that <c>specs/</c> is never hard-coded.
/// </summary>
public static class GroundingManifest
{
    public const string RelativePath = ".iskender/grounding.yaml";

    /// <summary>Returns the value of <c>section.key</c>, or null if the file or the key is missing.</summary>
    public static string? Read(IFileSet files, string section, string key)
    {
        if (files.ReadText(RelativePath) is not { } text)
        {
            return null;
        }

        var current = string.Empty;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                current = line.Split(':', 2)[0].Trim();
                continue;
            }

            var parts = line.Split(':', 2);
            if (parts.Length == 2
                && string.Equals(current, section, StringComparison.Ordinal)
                && string.Equals(parts[0].Trim(), key, StringComparison.Ordinal))
            {
                var value = parts[1].Trim().Trim('\'', '"');
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}

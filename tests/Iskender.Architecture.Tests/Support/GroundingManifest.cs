namespace Iskender.Architecture.Tests.Support;

/// <summary>
/// Reads .iskender/grounding.yaml line by line with no YAML package, tracking the section each key sits in,
/// so that neither docs/ nor specs/ is hard-coded.
/// </summary>
public static class GroundingManifest
{
    public const string RelativePath = ".iskender/grounding.yaml";

    /// <summary>Returns the value of <c>section.key</c>, or null if the file or the key is missing.</summary>
    public static string? Read(string root, string section, string key)
    {
        var path = Path.Combine(root, RelativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        var current = string.Empty;
        foreach (var line in File.ReadLines(path))
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

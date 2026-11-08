using System.Collections;
using System.Collections.Frozen;

namespace Iskender.Domain.Registry;

/// <summary>
/// A read-only set of environment variables. Only names starting with <c>ISKENDER__</c>, compared without regard to
/// case, are ever read (TR-5).
/// </summary>
public sealed class ConfigurationEnvironmentSource
{
    internal const string Prefix = "ISKENDER__";

    private ConfigurationEnvironmentSource(FrozenDictionary<string, string> variables)
    {
        Variables = variables;
    }

    /// <summary>The variables, by name.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    public static ConfigurationEnvironmentSource FromDictionary(IEnumerable<KeyValuePair<string, string>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return new(variables.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>Takes the process's variables whose names start with <c>ISKENDER__</c>. Nothing else is copied.</summary>
    public static ConfigurationEnvironmentSource FromProcess()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && HasPrefix(name))
            {
                variables[name] = entry.Value as string ?? string.Empty;
            }
        }

        return new(variables.ToFrozenDictionary(StringComparer.Ordinal));
    }

    internal static bool HasPrefix(string name) =>
        name.Length >= Prefix.Length && FieldRules.IgnoreAsciiCase.Equals(name[..Prefix.Length], Prefix);
}

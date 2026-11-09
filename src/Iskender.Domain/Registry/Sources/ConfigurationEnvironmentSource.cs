using System.Collections;
using System.Collections.Frozen;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// A read-only set of environment variables. Only names starting with <c>ISKENDER__</c>, compared without regard to
/// case, are ever read (TR-5).
/// </summary>
public sealed class ConfigurationEnvironmentSource
{
    internal const string Prefix = "ISKENDER__";

    // The same bound a document name has, since a variable's name is its location and part of its path (contract,
    // Environment variables rule 7), and both reach errors, the text rendering, and event 3002 (FR-020, FR-037).
    private const int MaxNameLength = 256;

    private ConfigurationEnvironmentSource(FrozenDictionary<string, string> variables)
    {
        // A name that is never read is never checked, so a host may pass its whole environment.
        if (variables.Keys.Any(name => HasPrefix(name) && (name.Length > MaxNameLength || name.Any(ConfigurationDocumentSource.IsBannedInName))))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Variables = variables;
    }

    /// <summary>The variables, by name. Each name that starts with <c>ISKENDER__</c> is at most 256 characters, with no control characters, U+2028, or U+2029.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    public static ConfigurationEnvironmentSource FromDictionary(IEnumerable<KeyValuePair<string, string>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return new(variables.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>Takes the process's variables whose names start with <c>ISKENDER__</c>. Nothing else is copied, and nothing else is checked.</summary>
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

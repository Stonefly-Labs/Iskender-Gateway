using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// A read-only view of one section, or of a section field or array item within it. Getters take a declared field name.
/// An absent value gives the field's default, or null when it has none. An undeclared name, or a getter that does not
/// match the declared type, fails with <c>config.section.invalid_access</c>. No member changes a value or returns a
/// writable collection. Members added later are virtual with a default, so a deriving type does not break.
/// </summary>
[ContractVersion("core-contracts", 2)]
public abstract class ConfigurationSection
{
    /// <summary>Fails with <c>core.value.invalid</c> for an empty namespace or path, or a version below 1.</summary>
    protected ConfigurationSection(string @namespace, int schemaVersion, string path)
    {
        if (string.IsNullOrEmpty(@namespace) || schemaVersion < 1 || string.IsNullOrEmpty(path))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Namespace = @namespace;
        SchemaVersion = schemaVersion;
        Path = path;
    }

    /// <summary>The namespace the section belongs to.</summary>
    public string Namespace { get; }

    /// <summary>The version of the schema that declares the namespace.</summary>
    public int SchemaVersion { get; }

    /// <summary>The path from <c>iskender</c>, such as <c>iskender.persistence</c> or <c>iskender.persistence.pools[0]</c>.</summary>
    public string Path { get; }

    /// <summary>A string or enumeration field.</summary>
    public abstract string? GetString(string name);

    /// <summary>An integer field.</summary>
    public abstract long? GetInt64(string name);

    /// <summary>A number field.</summary>
    public abstract decimal? GetDecimal(string name);

    /// <summary>A boolean field.</summary>
    public abstract bool? GetBoolean(string name);

    /// <summary>A duration field.</summary>
    public abstract TimeSpan? GetDuration(string name);

    /// <summary>A URI field.</summary>
    public abstract Uri? GetUri(string name);

    /// <summary>A secret reference field. Only the reference name is held; nothing here resolves it.</summary>
    public abstract SecretReference? GetSecretReference(string name);

    /// <summary>A section field. Never null, since every section field is always present.</summary>
    public abstract ConfigurationSection GetSection(string name);

    /// <summary>An array of section items.</summary>
    public abstract IReadOnlyList<ConfigurationSection>? GetSections(string name);

    /// <summary>An array of string or enumeration items.</summary>
    public abstract IReadOnlyList<string>? GetStrings(string name);

    /// <summary>An array of integer items.</summary>
    public abstract IReadOnlyList<long>? GetInt64s(string name);

    /// <summary>An array of number items.</summary>
    public abstract IReadOnlyList<decimal>? GetDecimals(string name);

    /// <summary>An array of boolean items.</summary>
    public abstract IReadOnlyList<bool>? GetBooleans(string name);

    /// <summary>An array of duration items.</summary>
    public abstract IReadOnlyList<TimeSpan>? GetDurations(string name);

    /// <summary>An array of URI items.</summary>
    public abstract IReadOnlyList<Uri>? GetUris(string name);

    /// <summary>An array of secret reference items.</summary>
    public abstract IReadOnlyList<SecretReference>? GetSecretReferences(string name);
}

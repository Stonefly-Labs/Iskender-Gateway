using System.Collections.Immutable;
using System.Security.Cryptography;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>The only snapshot implementation: values that never change, reachable only through the registered schema instances.</summary>
internal sealed class ValidatedSnapshot : ConfigurationSnapshot
{
    // SectionSchema does not override equality, so lookup is by the registered instance (research R11).
    private readonly Dictionary<SectionSchema, ValidatedSection> _sections;

    private ValidatedSnapshot(ConfigurationRevision revision, string? environmentName, ConfigurationLayers layers,
        IReadOnlyList<(SectionSchema Schema, SectionValues Values)> sections, byte[] canonical)
    {
        Revision = revision;
        EnvironmentName = environmentName;
        Layers = layers;
        Canonical = canonical;
        Namespaces = sections.Select(section => new SectionVersion(section.Schema.Namespace, section.Schema.Version))
            .OrderBy(version => version.Namespace, StringComparer.Ordinal)
            .ToImmutableArray();
        _sections = sections.ToDictionary(
            section => section.Schema,
            section => new ValidatedSection(section.Schema.Namespace, section.Schema.Version, $"{SchemaValidator.Root}.{section.Schema.Namespace}", section.Values));
    }

    public override ConfigurationRevision Revision { get; }

    public override string? EnvironmentName { get; }

    public override IReadOnlyList<SectionVersion> Namespaces { get; }

    public override ConfigurationLayers Layers { get; }

    /// <summary>The canonical bytes the revision was derived from.</summary>
    internal byte[] Canonical { get; }

    /// <summary>Writes the canonical form and derives the revision, <c>cfg-</c> and the base32 SHA-256 of those bytes.</summary>
    public static ValidatedSnapshot Create(IReadOnlyList<(SectionSchema Schema, SectionValues Values)> sections, string? environmentName, ConfigurationLayers layers)
    {
        var canonical = CanonicalWriter.Write(sections);
        var revision = new ConfigurationRevision("cfg-" + Base32Lower.Encode(SHA256.HashData(canonical)));
        return new ValidatedSnapshot(revision, environmentName, layers, sections, canonical);
    }

    public override ConfigurationSection GetSection(SectionSchema schema) =>
        schema is not null && _sections.TryGetValue(schema, out var section)
            ? section
            : throw Faults.Create(ErrorCodes.ConfigSectionInvalidAccess,
                ("namespace", schema?.Namespace ?? "(none)"), ("misuse", "the schema instance was not registered"));
}

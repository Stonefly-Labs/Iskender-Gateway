using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// A validated configuration that never changes (FR-029). Only the validator creates one, as a successful
/// <see cref="ConfigurationValidationReport"/>; in this repository only <c>Iskender.Domain.Registry</c> derives from
/// this class, which a test checks. Members added later are virtual with a default, so a deriving type does not break.
/// </summary>
[ContractVersion("core-contracts", 2)]
public abstract class ConfigurationSnapshot
{
    protected ConfigurationSnapshot()
    {
    }

    /// <summary>The revision derived from the snapshot's canonical content.</summary>
    public abstract ConfigurationRevision Revision { get; }

    /// <summary>The active environment name as given, or null when none was set.</summary>
    public abstract string? EnvironmentName { get; }

    /// <summary>Each registered namespace and its schema version, in ordinal order.</summary>
    public abstract IReadOnlyList<SectionVersion> Namespaces { get; }

    /// <summary>The sources the snapshot was built from.</summary>
    public abstract ConfigurationLayers Layers { get; }

    /// <summary>
    /// The section for the schema instance that was registered. Any other instance, including a copy with the same
    /// namespace, fails with <c>config.section.invalid_access</c> (FR-009).
    /// </summary>
    public abstract ConfigurationSection GetSection(SectionSchema schema);
}

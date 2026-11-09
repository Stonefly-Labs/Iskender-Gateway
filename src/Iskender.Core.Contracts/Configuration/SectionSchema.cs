using System.Collections.Immutable;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// A module's declaration of the section it owns under <c>iskender</c> (FR-006). Built with <see cref="Create"/> and
/// <see cref="WithFields"/>, which returns a new instance. The registry checks it when built (FR-008), and the instance
/// registered is the only one that can read the section from a snapshot (FR-009).
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class SectionSchema
{
    private SectionSchema(string @namespace, string ownerModule, int version, ImmutableArray<FieldSchema> fields)
    {
        Namespace = @namespace;
        OwnerModule = ownerModule;
        Version = version;
        Fields = fields;
    }

    /// <summary>1 to 32 characters of <c>a-z</c>, <c>0-9</c>, and <c>-</c>, starting with a letter.</summary>
    public string Namespace { get; }

    /// <summary>The owning module: 1 to 64 characters, used in diagnostics only.</summary>
    public string OwnerModule { get; }

    /// <summary>The schema version, 1 or more. Part of every revision, so changing it changes the revision.</summary>
    public int Version { get; }

    /// <summary>The declared fields.</summary>
    public IReadOnlyList<FieldSchema> Fields { get; }

    /// <summary>Starts a section declaration.</summary>
    public static SectionSchema Create(string @namespace, string ownerModule, int version) =>
        new(@namespace, ownerModule, version, []);

    /// <summary>Appends declared fields.</summary>
    public SectionSchema WithFields(params FieldSchema[] fields) =>
        new(Namespace, OwnerModule, Version, ((ImmutableArray<FieldSchema>)Fields).AddRange(fields ?? []));
}

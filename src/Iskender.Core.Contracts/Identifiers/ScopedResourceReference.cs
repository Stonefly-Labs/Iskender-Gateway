using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// A resource ID together with the owner scope a trusted resolver attested for it. It has no public constructor: the only
/// route to one is <see cref="OwnershipResolver"/> (FR-011, research R7).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ScopedResourceReference
{
    internal ScopedResourceReference(ResourceId id, OwnerScope owner)
    {
        Id = id;
        Owner = owner;
    }

    /// <summary>The resource's ID.</summary>
    public ResourceId Id { get; }

    /// <summary>The attested owner.</summary>
    public OwnerScope Owner { get; }
}

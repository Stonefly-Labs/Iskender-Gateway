using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// The base of a trusted ownership resolver, the only route to a <see cref="ScopedResourceReference"/>. Only the types
/// listed for rule H6 in the published contract may derive from it, and that list starts empty (research R7).
/// </summary>
[ContractVersion("core-contracts", 1)]
public abstract class OwnershipResolver
{
    /// <summary>Records that the resolver established <paramref name="owner"/> as the owner of <paramref name="id"/>.</summary>
    protected static ScopedResourceReference Attest(ResourceId id, OwnerScope owner)
    {
        if (id is null || owner is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new ScopedResourceReference(id, owner);
    }
}

using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>The caller's resolved principal, owned by the identity specs (ISK-0011 onward). It is empty until they add members, which is a compatible change (FR-015).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class PrincipalView
{
    internal PrincipalView()
    {
    }
}

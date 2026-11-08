using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>The request's routing attempts, owned by the routing specs. It is empty until they add members, which is a compatible change (FR-015).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class RoutingAttemptView
{
    internal RoutingAttemptView()
    {
    }
}

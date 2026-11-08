using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>The request's policy execution, owned by the policy specs (ISK-0052 onward). It is empty until they add members, which is a compatible change (FR-015).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class PolicyExecutionView
{
    internal PolicyExecutionView()
    {
    }
}

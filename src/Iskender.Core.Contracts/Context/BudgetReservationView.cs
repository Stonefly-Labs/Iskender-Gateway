using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>The request's budget reservations, owned by the governance specs. It is empty until they add members, which is a compatible change (FR-015).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class BudgetReservationView
{
    internal BudgetReservationView()
    {
    }
}

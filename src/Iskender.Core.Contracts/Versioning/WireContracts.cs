using System.Diagnostics.CodeAnalysis;

namespace Iskender.Core.Contracts.Versioning;

/// <summary>The registry of wire contracts. The published contract's Wire contracts table must match it (rule H4).</summary>
[ContractVersion("core-contracts", 1)]
public static class WireContracts
{
    /// <summary>The client-visible error envelope.</summary>
    public static readonly WireContract ErrorEnvelope = new("error-envelope", 1, 1);

    /// <summary>The text form of a resource ID.</summary>
    public static readonly WireContract ResourceId = new("resource-id", 1, 1);

    /// <summary>Every registered wire contract, in contract order.</summary>
    public static IReadOnlyList<WireContract> All { get; } = Array.AsReadOnly([ErrorEnvelope, ResourceId]);

    /// <summary>Finds a wire contract by its ID, compared ordinally.</summary>
    public static bool TryGet(string id, [NotNullWhen(true)] out WireContract? contract)
    {
        contract = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return contract is not null;
    }
}

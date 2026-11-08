namespace Iskender.Core.Contracts.Versioning;

/// <summary>Records the contract version that introduced a public type, or a member added after its type.</summary>
[ContractVersion("core-contracts", 1)]
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate
        | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ContractVersionAttribute : Attribute
{
    public ContractVersionAttribute(string contractId, int since)
    {
        ContractId = contractId;
        Since = since;
    }

    /// <summary>The contract ID, which equals the assembly's <see cref="ContractPackageAttribute.ContractId"/>.</summary>
    public string ContractId { get; }

    /// <summary>The contract version that introduced the item. It never changes once published.</summary>
    public int Since { get; }
}

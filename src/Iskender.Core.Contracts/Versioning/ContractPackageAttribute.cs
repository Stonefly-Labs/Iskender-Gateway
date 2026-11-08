namespace Iskender.Core.Contracts.Versioning;

/// <summary>Declares the contract a public contract assembly implements and that contract's current version.</summary>
[ContractVersion("core-contracts", 1)]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ContractPackageAttribute : Attribute
{
    public ContractPackageAttribute(string contractId, int version)
    {
        ContractId = contractId;
        Version = version;
    }

    /// <summary>The contract ID, such as <c>core-contracts</c>.</summary>
    public string ContractId { get; }

    /// <summary>The contract's current version.</summary>
    public int Version { get; }
}

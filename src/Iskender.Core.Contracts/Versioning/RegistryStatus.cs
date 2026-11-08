namespace Iskender.Core.Contracts.Versioning;

/// <summary>Whether a registry entry, such as an error code or resource type, is still accepted. A retired entry stays registered.</summary>
[ContractVersion("core-contracts", 1)]
public enum RegistryStatus
{
    /// <summary>The entry is in use.</summary>
    Active = 0,

    /// <summary>The entry is no longer accepted. Its ID is never reused.</summary>
    Retired = 1,
}

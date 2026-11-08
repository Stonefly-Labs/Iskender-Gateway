using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>Whether an error held detail that its envelope does not show.</summary>
[ContractVersion("core-contracts", 1)]
public enum RedactionStatus
{
    /// <summary>Nothing was withheld.</summary>
    None = 0,

    /// <summary>Diagnostics or provider detail were withheld, or the category is <c>not_found</c> (TR-3).</summary>
    Withheld = 1,
}

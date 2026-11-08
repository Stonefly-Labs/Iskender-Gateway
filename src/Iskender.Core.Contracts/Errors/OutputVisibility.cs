using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>Whether streamed output had reached the client when the error occurred.</summary>
[ContractVersion("core-contracts", 1)]
public enum OutputVisibility
{
    /// <summary>No output was visible.</summary>
    None = 0,

    /// <summary>Output was visible, so no retry advice is permitted (FR-025).</summary>
    Visible = 1,
}

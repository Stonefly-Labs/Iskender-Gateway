using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>When an error category permits retry advice.</summary>
[ContractVersion("core-contracts", 1)]
public enum RetryRule
{
    /// <summary>Retry advice is never permitted.</summary>
    Never = 0,

    /// <summary>Permitted when the limit states when it resets.</summary>
    WhenResetKnown = 1,

    /// <summary>Permitted before any output only.</summary>
    BeforeOutputOnly = 2,

    /// <summary>Permitted before any output, and only when the mapped failure is retriable.</summary>
    BeforeOutputWhenRetriable = 3,
}

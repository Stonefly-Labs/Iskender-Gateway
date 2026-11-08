using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// Advice that the caller may retry. <see cref="GatewayError.Create"/> accepts it only where the category's
/// <see cref="RetryRule"/> and the output visibility permit (FR-020, FR-024, FR-025).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class RetryAdvice
{
    public RetryAdvice(TimeSpan? retryAfter = null, bool retriable = false)
    {
        if (retryAfter < TimeSpan.Zero)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        RetryAfter = retryAfter;
        Retriable = retriable;
    }

    /// <summary>How long to wait before retrying, when known. Never negative. Serialized in whole seconds, rounded up.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>For <c>upstream_failed</c>: whether the mapped upstream failure is retriable.</summary>
    public bool Retriable { get; }
}

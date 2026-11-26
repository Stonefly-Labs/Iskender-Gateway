using System.Threading.RateLimiting;

namespace Iskender.Management.Security;

/// <summary>
/// The redemption attempt limit (FR-012, operator-security contract Admin routes rule 5): a one-minute fixed window of
/// <c>operator.bootstrap.attemptsPerMinute</c> permits with no queue, one per service container, across both redemption
/// routes. Registered under this Iskender-owned type rather than the shared <see cref="RateLimiter"/>, so a host's own
/// <see cref="RateLimiter"/> registration neither changes this limit nor is replaced by it (hosting contract, Coexistence
/// rule 1; CONV-0010-019). The container disposes it. Owned by ISK-0010.
/// </summary>
internal sealed class OperatorAttemptLimiter(int attemptsPerMinute) : IDisposable
{
    private readonly FixedWindowRateLimiter _limiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = attemptsPerMinute,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    });

    /// <summary>Takes one attempt's permit; the lease is not acquired when the window's permits are spent.</summary>
    public RateLimitLease Acquire() => _limiter.AttemptAcquire();

    /// <inheritdoc />
    public void Dispose() => _limiter.Dispose();
}

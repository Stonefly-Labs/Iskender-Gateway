using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>How long to wait for an advisory lock: not at all, or up to a positive bound. Owned by ISK-0004.</summary>
public sealed class AdvisoryLockWait
{
    private AdvisoryLockWait(TimeSpan? bound)
    {
        Bound = bound;
    }

    /// <summary>Returns <c>false</c> at once when the lock is held.</summary>
    public static AdvisoryLockWait TryOnly { get; } = new(bound: null);

    /// <summary>The wait's bound, or <c>null</c> for <see cref="TryOnly"/>.</summary>
    public TimeSpan? Bound { get; }

    /// <summary>Waits up to <paramref name="bound"/>, which must be positive.</summary>
    public static AdvisoryLockWait Bounded(TimeSpan bound) =>
        bound > TimeSpan.Zero ? new(bound) : throw new GatewayException(ErrorCodes.ValueInvalid);
}

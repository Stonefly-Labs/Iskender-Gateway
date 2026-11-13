using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// How long to wait for a held lease: not at all, or up to a positive bound. The coordinator also checks the bound against
/// <c>leases.maximumWait</c>. Owned by ISK-0005.
/// </summary>
public sealed class LeaseWait
{
    private LeaseWait(TimeSpan? bound)
    {
        Bound = bound;
    }

    /// <summary>Reports contention at once when the lease is held.</summary>
    public static LeaseWait TryOnly { get; } = new(bound: null);

    /// <summary>The wait's bound, or <c>null</c> for <see cref="TryOnly"/>.</summary>
    public TimeSpan? Bound { get; }

    /// <summary>Polls up to <paramref name="bound"/>, which must be positive, or fails with <c>core.value.invalid</c>.</summary>
    public static LeaseWait Bounded(TimeSpan bound) =>
        bound > TimeSpan.Zero ? new(bound) : throw new GatewayException(ErrorCodes.ValueInvalid);
}

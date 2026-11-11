using System.Diagnostics.CodeAnalysis;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>An acquisition's result: the lease, or contention. Contention is a result, not an error (FR-015). Owned by ISK-0005.</summary>
public sealed class LeaseAcquisition
{
    private LeaseAcquisition(Lease? lease)
    {
        Lease = lease;
    }

    /// <summary>Another holder held the lease.</summary>
    public static LeaseAcquisition Contended { get; } = new(lease: null);

    /// <summary>The granted lease, or <c>null</c> when contended.</summary>
    public Lease? Lease { get; }

    [MemberNotNullWhen(true, nameof(Lease))]
    public bool IsAcquired => Lease is not null;

    /// <summary>A granted lease. A <c>null</c> lease fails with <c>core.value.invalid</c>.</summary>
    public static LeaseAcquisition Acquired(Lease lease) => new(lease ?? throw new GatewayException(ErrorCodes.ValueInvalid));
}

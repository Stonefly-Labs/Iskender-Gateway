namespace Iskender.Domain.Ports;

/// <summary>
/// Leases with fencing tokens (FR-014 to FR-016, TR-9). A duration outside <c>[leases.minimumDuration,
/// leases.maximumDuration]</c> or a wait above <c>leases.maximumWait</c> fails with <c>core.value.invalid</c> before any
/// I/O. Expiry is measured on the coordinator's clock. Neither leases nor their tokens use an advisory lock (FR-018).
/// Owned by ISK-0005.
/// </summary>
public interface ILeaseCoordinator
{
    /// <summary>
    /// Acquires the lease <paramref name="name"/> in <paramref name="scope"/> for <paramref name="duration"/>, with a new
    /// fencing token strictly greater than every token issued for that key. A lease held by another holder is reported as
    /// <see cref="LeaseAcquisition.Contended"/> at once for <see cref="LeaseWait.TryOnly"/>, or when a bounded wait ends.
    /// Bounded waits poll and give no fairness or ordering between waiters.
    /// </summary>
    ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken);

    /// <summary>
    /// Extends a held lease to <paramref name="duration"/> from now, keeping its token. A lease that expired or was taken
    /// by another holder fails with <c>coordination.lease_lost</c>.
    /// </summary>
    ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken);

    /// <summary>Releases the lease if the caller still holds it. Idempotent, and never releases another holder's lease.</summary>
    ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken);
}

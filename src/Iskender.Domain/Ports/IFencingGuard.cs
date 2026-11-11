namespace Iskender.Domain.Ports;

/// <summary>
/// Protects a resource in PostgreSQL with a lease's fencing token (FR-017, contract Leases and fencing rule 6). It runs
/// inside the caller's unit of work, whose scope must be the lease's scope, before the caller's protected write. It
/// accepts a token only when it is at least the highest token accepted for the resource and comes from the lease key the
/// resource was first fenced with. Owned by ISK-0005.
/// </summary>
public interface IFencingGuard
{
    /// <summary>
    /// Records <paramref name="lease"/>'s token for <paramref name="resource"/> in <paramref name="unit"/>. A lease not
    /// issued by the PostgreSQL coordinator fails with <c>coordination.capability_unsupported</c> before any write. A unit
    /// in another scope fails with <c>coordination.scope_required</c>. A stale token, or one from another lease key,
    /// fails with <c>coordination.fencing_rejected</c>, and the unit faults and commits nothing. It uses no advisory lock.
    /// </summary>
    ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken cancellationToken);
}

namespace Iskender.Domain.Ports;

/// <summary>
/// Enqueues a work item in the caller's unit of work, so the item exists after the unit commits and does not after it
/// rolls back (work contract, The outbox). Owned by ISK-0009.
/// </summary>
public interface IWorkOutbox
{
    /// <summary>
    /// Writes the item and its scope's directory registration in <paramref name="unit"/>'s transaction only. Before any
    /// write it fails with <c>work.unit_not_open</c> when the unit is not open, <c>work.scope_mismatch</c> when the
    /// request's owner is not the unit's, <c>work.kind_undeclared</c> when the kind or its payload version is not
    /// declared, or <c>work.payload_too_large</c> when the payload is over 65,536 bytes, and then the unit is faulted so
    /// that it commits nothing. A retained item with the same scope, kind, and key is returned with <c>Created</c>
    /// <c>false</c>, unchanged.
    /// </summary>
    ValueTask<WorkEnqueueResult> EnqueueAsync(IUnitOfWork unit, WorkItemRequest request, CancellationToken cancellationToken);
}

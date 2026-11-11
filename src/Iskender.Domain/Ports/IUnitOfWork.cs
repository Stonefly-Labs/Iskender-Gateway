namespace Iskender.Domain.Ports;

/// <summary>
/// One scoped transaction on one connection (FR-004, FR-011). It commits only on <see cref="CommitAsync"/>, and disposing
/// it otherwise rolls it back. It serves one logical operation at a time: a second concurrent call fails with
/// <c>core.internal</c> and faults the unit. Every failure is a <c>GatewayException</c>. Owned by ISK-0004.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>The scope it was opened with.</summary>
    UnitOfWorkScope Scope { get; }

    UnitOfWorkIsolation Isolation { get; }

    UnitOfWorkState State { get; }

    /// <summary>What faulted the unit, or <c>null</c>. Set before the exception is thrown, and never cleared.</summary>
    PersistenceFailure? Failure { get; }

    /// <summary>Commits. Allowed once, in <see cref="UnitOfWorkState.Open"/>.</summary>
    ValueTask CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Takes a transaction-scoped advisory lock in the unit's own scope, released at commit or rollback (FR-027). It is
    /// not a lease or a fencing token (FR-028). <see cref="AdvisoryLockWait.TryOnly"/> returns <c>false</c> when the lock
    /// is held and leaves the unit open. A bounded wait returns <c>true</c>, or fails with <c>persistence.lock_timeout</c>
    /// and faults the unit.
    /// </summary>
    ValueTask<bool> AcquireAdvisoryLockAsync(AdvisoryLockName name, AdvisoryLockWait wait, CancellationToken cancellationToken);
}

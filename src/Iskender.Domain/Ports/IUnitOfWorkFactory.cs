namespace Iskender.Domain.Ports;

/// <summary>The only way to reach the database from the ports (FR-004). Owned by ISK-0004.</summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Opens one connection and one transaction in <paramref name="scope"/>. Fails with <c>persistence.scope_required</c>,
    /// <c>persistence.timeout</c>, <c>persistence.cancelled</c>, or <c>persistence.unavailable</c> before returning a unit.
    /// </summary>
    ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken);
}

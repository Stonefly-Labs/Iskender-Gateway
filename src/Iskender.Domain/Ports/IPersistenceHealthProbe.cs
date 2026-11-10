namespace Iskender.Domain.Ports;

/// <summary>
/// Reports whether the store answers and its schema suits this release (FR-025). It never throws for a database
/// condition. The caller's token firing throws <c>persistence.cancelled</c>. Liveness never calls it. Owned by ISK-0004.
/// </summary>
public interface IPersistenceHealthProbe
{
    ValueTask<PersistenceHealthResult> CheckAsync(CancellationToken cancellationToken);
}

namespace Iskender.Domain.Ports;

/// <summary>Where a unit of work is in its life. Owned by ISK-0004.</summary>
public enum UnitOfWorkState
{
    Open,
    Committed,
    RolledBack,
    Faulted,
    Closed,
}

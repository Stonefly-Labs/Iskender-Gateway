namespace Iskender.Domain.Ports;

/// <summary>A unit of work's transaction isolation level (FR-012). Owned by ISK-0004.</summary>
public enum UnitOfWorkIsolation
{
    ReadCommitted,
    RepeatableRead,
    Serializable,
}

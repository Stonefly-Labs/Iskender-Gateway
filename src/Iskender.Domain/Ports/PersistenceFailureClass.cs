namespace Iskender.Domain.Ports;

/// <summary>
/// The fixed classes a persistence failure falls into (research R8). <see cref="ScopeViolation"/> covers
/// <c>persistence.scope_required</c> and <c>persistence.scope_not_permitted</c>, which come from the adapter, not the
/// database. Owned by ISK-0004.
/// </summary>
public enum PersistenceFailureClass
{
    TransientConflict,
    ConcurrencyConflict,
    UniqueConflict,
    Unavailable,
    Timeout,
    Cancelled,
    AuthenticationFailed,
    ScopeViolation,
    Unexpected,
}

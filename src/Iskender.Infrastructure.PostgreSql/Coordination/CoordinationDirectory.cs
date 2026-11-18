using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// Registers a scope in the scope directory under module <c>coordination</c> when a coordinator write inserted a new
/// expiring row, with the row's expiry as the due time, in the write's own unit (coordination contract Atomic operations
/// rule 7, ISK-0009 research R3). Each statement that can insert returns <c>(xmax = 0)</c>, which is true only for a row it
/// inserted; an update registers nothing, because a row of the scope exists and so its directory row does. Owned by
/// ISK-0009.
/// </summary>
internal static class CoordinationDirectory
{
    /// <summary>The directory module the coordinator registers under: the <c>coordination</c> migration module's name.</summary>
    public const string Module = CoordinationMigrations.Name;

    /// <summary>Registers the unit's scope when <paramref name="inserted"/>, due at <paramref name="expiresAt"/>.</summary>
    public static ValueTask RegisterAsync(IUnitOfWork unit, bool inserted, DateTime expiresAt, CancellationToken cancellationToken) =>
        inserted ? ScopeDirectory.RegisterAsync(unit, Module, expiresAt, cancellationToken) : ValueTask.CompletedTask;
}

using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>The unit options of the identity stores: module <c>identity</c>, Read Committed, operator scope. Owned by ISK-0010.</summary>
internal static class IdentityUnits
{
    /// <summary>The bound on each identity unit, so a store that stopped answering fails rather than hangs.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static UnitOfWorkOptions Options(string operation) =>
        new(IdentityMigrations.Name, operation) { Isolation = UnitOfWorkIsolation.ReadCommitted, Timeout = Timeout };

    /// <summary>Opens an operator-scope unit for <paramref name="operation"/>.</summary>
    public static ValueTask<IUnitOfWork> OpenAsync(IUnitOfWorkFactory units, string operation, CancellationToken cancellationToken) =>
        units.OpenAsync(UnitOfWorkScope.Operator, Options(operation), cancellationToken);
}

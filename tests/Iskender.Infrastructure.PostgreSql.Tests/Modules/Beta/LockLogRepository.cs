using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

/// <summary>The fixture repository for <c>isk_beta.lock_log</c>, which records the order advisory lock holders ran in.</summary>
internal static class LockLogRepository
{
    public static async Task AppendAsync(IUnitOfWork unit, string holder, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(BetaTables.LockLog, "INSERT INTO isk_beta.lock_log (owner_organization_id, holder) VALUES ($1, $2)")
            .With(ScopeOwner.Organization(unit)).With(holder).ExecuteAsync(cancellationToken);

    /// <summary>The holders in the order they appended.</summary>
    public static async Task<IReadOnlyList<string>> ReadAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(BetaTables.LockLog, "SELECT holder FROM isk_beta.lock_log WHERE owner_organization_id = $1 ORDER BY seq")
            .With(ScopeOwner.Organization(unit)).QueryAsync(row => row.GetString(0), cancellationToken);
}

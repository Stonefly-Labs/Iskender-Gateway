using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

/// <summary>The fixture repository for <c>isk_beta.counters</c>, an organization-owned table, with an owner predicate a test can omit.</summary>
internal sealed class CounterRepository(bool ownerPredicate = true)
{
    private string Owner(int parameter) => ownerPredicate ? $" AND owner_organization_id = ${parameter}" : "";

    public static async Task CreateAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(BetaTables.Counters, "INSERT INTO isk_beta.counters (id, owner_organization_id) VALUES ($1, $2)")
            .With(id).With(ScopeOwner.Organization(unit)).ExecuteAsync(cancellationToken);

    /// <summary>The counter's value, or <c>core.resource.not_found</c> for a missing or foreign one alike.</summary>
    public async Task<long> GetAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(BetaTables.Counters, "SELECT value FROM isk_beta.counters WHERE id = $1" + Owner(2)).With(id);
        var rows = await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).QueryAsync(row => row.GetInt64(0), cancellationToken);
        return rows.Count == 1 ? rows[0] : throw new GatewayException(ErrorCodes.ResourceNotFound);
    }

    public async Task<int> IncrementAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(BetaTables.Counters, "UPDATE isk_beta.counters SET value = value + 1 WHERE id = $1" + Owner(2)).With(id);
        return await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).ExecuteAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(BetaTables.Counters, "DELETE FROM isk_beta.counters WHERE id = $1" + Owner(2)).With(id);
        return await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).ExecuteAsync(cancellationToken);
    }
}

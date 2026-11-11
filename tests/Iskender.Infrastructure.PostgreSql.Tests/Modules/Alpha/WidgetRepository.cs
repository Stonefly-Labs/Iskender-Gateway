using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

internal sealed record Widget(string Id, string OwnerOrganizationId, string Name, long RowVersion);

/// <summary>
/// The fixture repository for <c>isk_alpha.widgets</c>, an organization-owned table. Every statement goes through
/// <c>Command(TableAccess, sql)</c> with positional parameters and an owner predicate, which
/// <paramref name="ownerPredicate"/> false omits so a test can prove row-level security alone.
/// </summary>
internal sealed class WidgetRepository(bool ownerPredicate = true)
{
    private string Owner(int parameter) => ownerPredicate ? $" AND owner_organization_id = ${parameter}" : "";

    public static async Task InsertAsync(IUnitOfWork unit, string id, string name, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Widgets, "INSERT INTO isk_alpha.widgets (id, owner_organization_id, name) VALUES ($1, $2, $3)")
            .With(id).With(ScopeOwner.Organization(unit)).With(name).ExecuteAsync(cancellationToken);

    /// <summary>The row, or <c>core.resource.not_found</c> for a missing or foreign one alike.</summary>
    public async Task<Widget> GetAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Widgets, "SELECT id, owner_organization_id, name, row_version FROM isk_alpha.widgets WHERE id = $1" + Owner(2)).With(id);
        var rows = await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).QueryAsync(Map, cancellationToken);
        return rows.Count == 1 ? rows[0] : throw new GatewayException(ErrorCodes.ResourceNotFound);
    }

    public async Task<IReadOnlyList<Widget>> ListAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Widgets, "SELECT id, owner_organization_id, name, row_version FROM isk_alpha.widgets WHERE true" + Owner(1) + " ORDER BY id");
        return await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).QueryAsync(Map, cancellationToken);
    }

    /// <summary>The number of rows renamed: 0 for a missing or foreign row.</summary>
    public async Task<int> RenameAsync(IUnitOfWork unit, string id, string name, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Widgets, "UPDATE isk_alpha.widgets SET name = $2, row_version = row_version + 1 WHERE id = $1" + Owner(3)).With(id).With(name);
        return await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).ExecuteAsync(cancellationToken);
    }

    /// <summary>The number of rows deleted: 0 for a missing or foreign row.</summary>
    public async Task<int> DeleteAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var command = PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Widgets, "DELETE FROM isk_alpha.widgets WHERE id = $1" + Owner(2)).With(id);
        return await (ownerPredicate ? command.With(ScopeOwner.Organization(unit)) : command).ExecuteAsync(cancellationToken);
    }

    /// <summary>Renames the row if it still has <paramref name="expected"/>, and returns its next version (FR-016).</summary>
    public static ValueTask<VersionToken> RenameVersionedAsync(IUnitOfWork unit, string id, VersionToken expected, string name, CancellationToken cancellationToken) =>
        VersionedRows.UpdateAsync(unit, AlphaTables.Widgets, id, expected, "name = $3", [name], cancellationToken);

    /// <summary>Deletes the row if it still has <paramref name="expected"/>.</summary>
    public static ValueTask DeleteVersionedAsync(IUnitOfWork unit, string id, VersionToken expected, CancellationToken cancellationToken) =>
        VersionedRows.DeleteAsync(unit, AlphaTables.Widgets, id, expected, cancellationToken);

    private static Widget Map(Npgsql.NpgsqlDataReader row) => new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetInt64(3));
}

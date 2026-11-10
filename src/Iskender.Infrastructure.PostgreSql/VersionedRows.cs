using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// Optimistic update and delete on a versioned table, one with <c>row_version bigint not null default 1</c> (FR-016,
/// research R10). A statement runs with <c>WHERE id = $1 AND row_version = $2</c>. When it changes nothing, one
/// <c>SELECT 1</c> in the same unit, under the same row-level security, tells a stale token
/// (<c>persistence.concurrency_conflict</c>, which faults the unit) from a missing or foreign row
/// (<c>core.resource.not_found</c>). Owned by ISK-0004.
/// </summary>
internal static class VersionedRows
{
    /// <summary>
    /// Runs <c>UPDATE &lt;table&gt; SET &lt;set&gt;, row_version = row_version + 1</c>, where <paramref name="set"/>
    /// names its values as <c>$3</c> onwards, and returns the next token.
    /// </summary>
    public static async ValueTask<VersionToken> UpdateAsync(IUnitOfWork unit, TableAccess table, string id, VersionToken expected, string set, IReadOnlyList<object?> values, CancellationToken cancellationToken)
    {
        var adapter = PostgreSqlUnitOfWork.From(unit);
        var command = adapter.Command(table, $"UPDATE {table.QualifiedName} SET {set}, row_version = row_version + 1 WHERE id = $1 AND row_version = $2 RETURNING row_version")
            .With(id).With(expected.Value);
        foreach (var value in values)
        {
            command.With(value);
        }

        var next = await command.QueryAsync(row => row.GetInt64(0), cancellationToken);
        return next.Count == 1 ? new VersionToken(next[0]) : throw await NoRowChangedAsync(adapter, table, id, cancellationToken);
    }

    /// <summary>Deletes the row if it still has <paramref name="expected"/>.</summary>
    public static async ValueTask DeleteAsync(IUnitOfWork unit, TableAccess table, string id, VersionToken expected, CancellationToken cancellationToken)
    {
        var adapter = PostgreSqlUnitOfWork.From(unit);
        var deleted = await adapter.Command(table, $"DELETE FROM {table.QualifiedName} WHERE id = $1 AND row_version = $2").With(id).With(expected.Value).ExecuteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw await NoRowChangedAsync(adapter, table, id, cancellationToken);
        }
    }

    private static async ValueTask<GatewayException> NoRowChangedAsync(PostgreSqlUnitOfWork unit, TableAccess table, string id, CancellationToken cancellationToken)
    {
        var visible = await unit.Command(table, $"SELECT 1 FROM {table.QualifiedName} WHERE id = $1").With(id).ScalarAsync<int>(cancellationToken) == 1;
        return visible
            ? unit.Fault(FailureClassifier.Raised(ErrorCodes.PersistenceConcurrencyConflict, PersistenceFailureClass.ConcurrencyConflict))
            : new GatewayException(ErrorCodes.ResourceNotFound);
    }
}

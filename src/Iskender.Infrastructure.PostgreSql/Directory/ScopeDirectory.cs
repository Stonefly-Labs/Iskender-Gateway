using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Directory;

/// <summary>
/// What remains of a module's work in one scope, as the owning namespace's <c>next</c> query reports it to a settle
/// (ISK-0009 research R3): nothing, or work whose earliest due time is <see cref="DueAt"/>, <c>null</c> when none is due.
/// Owned by ISK-0009.
/// </summary>
internal readonly record struct DirectoryRemaining(bool Remains, DateTime? DueAt)
{
    public static DirectoryRemaining None { get; } = new(Remains: false, DueAt: null);

    public static DirectoryRemaining Due(DateTime? dueAt) => new(Remains: true, dueAt);
}

/// <summary>How a settle left a scope's directory row. Owned by ISK-0009.</summary>
internal enum DirectorySettlement
{
    /// <summary>There was no row.</summary>
    Absent,

    /// <summary>No work remained, so the row was deleted.</summary>
    Removed,

    /// <summary>Work remained, so the row was kept with the earliest due time.</summary>
    Kept,
}

/// <summary>
/// Registers and settles a scope's row in <c>isk_directory.scopes</c> (persistence contract Isolation rule 10, ISK-0009
/// research R3). Register runs in the producer's own unit at any isolation, in the same transaction as the write that
/// needs the row. Settle runs only in a framework unit at Read Committed. Neither uses an advisory lock or a function that
/// bypasses row-level security, and every statement names the unit's own scope, so no unit touches another scope's row.
/// Owned by ISK-0009.
/// </summary>
internal static class ScopeDirectory
{
    /// <summary>Register gives up after this many rounds of a settle deleting the row it saw.</summary>
    public const int MaximumRounds = 3;

    private const string InsertSql =
        "INSERT INTO isk_directory.scopes (listed_scope, module, due_at) VALUES ($1, $2, $3::timestamptz) ON CONFLICT DO NOTHING RETURNING 1";

    // The producers' FOR KEY SHARE locks do not conflict with each other, only with a settle's FOR UPDATE.
    private const string LockForRegisterSql =
        "SELECT (due_at IS NULL OR due_at > $3::timestamptz) FROM isk_directory.scopes WHERE {listed} AND module = $2 FOR KEY SHARE";

    private const string LowerSql =
        "UPDATE isk_directory.scopes SET due_at = $3::timestamptz WHERE {listed} AND module = $2 AND (due_at IS NULL OR due_at > $3::timestamptz)";

    private const string LockForSettleSql = "SELECT 1 FROM isk_directory.scopes WHERE {listed} AND module = $2 FOR UPDATE";

    private const string DeleteSql = "DELETE FROM isk_directory.scopes WHERE {listed} AND module = $2";

    private const string SetDueSql = "UPDATE isk_directory.scopes SET due_at = $3::timestamptz WHERE {listed} AND module = $2";

    /// <summary>
    /// Makes sure the unit's scope has a row for <paramref name="module"/> with a due time no later than
    /// <paramref name="due"/>. A settle that deletes the row between the insert and the lock sends it round again, at
    /// most <see cref="MaximumRounds"/> times, then the unit faults with <c>persistence.concurrency_conflict</c>. At
    /// Repeatable Read or Serializable, a race with a settle fails the unit with the same code, from SQLSTATE 40001.
    /// </summary>
    public static async ValueTask RegisterAsync(IUnitOfWork unit, string module, DateTime due, CancellationToken cancellationToken)
    {
        var adapter = PostgreSqlUnitOfWork.From(unit);
        var utc = Utc(due);
        for (var round = 0; round < MaximumRounds; round++)
        {
            if (await adapter.Command(DirectoryTables.Scopes, InsertSql).With(module).With(utc).ScalarAsync<int?>(cancellationToken) is not null)
            {
                return;
            }

            var lower = await adapter.Command(DirectoryTables.Scopes, LockForRegisterSql).With(module).With(utc).QueryAsync(reader => reader.GetBoolean(0), cancellationToken);
            if (lower.Count == 0)
            {
                continue;
            }

            if (lower[0])
            {
                await adapter.Command(DirectoryTables.Scopes, LowerSql).With(module).With(utc).ExecuteAsync(cancellationToken);
            }

            return;
        }

        throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.PersistenceConcurrencyConflict, PersistenceFailureClass.TransientConflict));
    }

    /// <summary>
    /// Locks the unit's scope's row for <paramref name="module"/>, then runs <paramref name="next"/> as a new statement,
    /// which sees any producer that committed while the lock waited, and deletes the row when nothing remains or sets its
    /// due time otherwise. The caller commits. A unit that is not Read Committed faults with <c>core.internal</c> before
    /// any statement.
    /// </summary>
    public static async ValueTask<DirectorySettlement> SettleAsync(
        IUnitOfWork unit,
        string module,
        Func<CancellationToken, ValueTask<DirectoryRemaining>> next,
        CancellationToken cancellationToken)
    {
        var adapter = PostgreSqlUnitOfWork.From(unit);
        if (unit.Isolation != UnitOfWorkIsolation.ReadCommitted)
        {
            throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.Internal, PersistenceFailureClass.Unexpected));
        }

        var locked = await adapter.Command(DirectoryTables.Scopes, LockForSettleSql).With(module).QueryAsync(_ => true, cancellationToken);
        if (locked.Count == 0)
        {
            return DirectorySettlement.Absent;
        }

        var remaining = await next(cancellationToken);
        if (!remaining.Remains)
        {
            await adapter.Command(DirectoryTables.Scopes, DeleteSql).With(module).ExecuteAsync(cancellationToken);
            return DirectorySettlement.Removed;
        }

        await adapter.Command(DirectoryTables.Scopes, SetDueSql).With(module).With(remaining.DueAt is { } dueAt ? Utc(dueAt) : null).ExecuteAsync(cancellationToken);
        return DirectorySettlement.Kept;
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
}

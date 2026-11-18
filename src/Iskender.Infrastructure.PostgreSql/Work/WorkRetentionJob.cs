using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The shipped job <c>work.retention</c> (<c>work</c> contract Recurring jobs rule 5, FR-021, research R10): it pages
/// every <c>work</c> row of the scope directory, due or not, in an operator unit, and for each scope deletes
/// <c>completed</c> items finished more than <c>retention.completed</c> ago and <c>poisoned</c> items finished more than
/// <c>retention.poisoned</c> ago, on the database clock, a batch at a time in the scope's own unit, then settles the
/// scope's <c>work</c> row, so a scope with no item left loses it. A deleted item's idempotency key can enqueue a new item,
/// which bounds the dedupe window (The outbox rule 6). It saves the last scope of each page as its cursor. A malformed
/// row is skipped with event 8009. Owned by ISK-0009.
/// </summary>
internal sealed class WorkRetentionJob : IRecurringJob
{
    private const string DeleteSql =
        "DELETE FROM isk_work.items WHERE {scope} AND item_id IN (SELECT item_id FROM isk_work.items WHERE {scope} AND state <> 'pending' AND "
        + "((state = 'completed' AND finished_at < clock_timestamp() - $2::interval) OR (state = 'poisoned' AND finished_at < clock_timestamp() - $3::interval)) LIMIT $4)";

    private readonly IUnitOfWorkFactory _units;
    private readonly WorkSettings _settings;
    private readonly ILogger _logger;

    public WorkRetentionJob(IUnitOfWorkFactory units, WorkSettings settings, ILoggerFactory? loggers = null)
    {
        _units = units;
        _settings = settings;
        _logger = loggers?.CreateLogger(WorkLog.Category) ?? NullLogger.Instance;
    }

    /// <summary>The registration <c>AddIskenderPostgreSqlWorkDispatcher()</c> adds: every hour, 500 at a time, by default.</summary>
    public static RecurringJobRegistration Registration { get; } = new(WorkSettings.RetentionJobName, TimeSpan.FromHours(1), 500, typeof(WorkRetentionJob));

    public string Name => WorkSettings.RetentionJobName;

    public async ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<string> page;
            await using (var unit = await _units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions(ShardLeases.Module, "work.retention_page"), cancellationToken))
            {
                page = await ScopeDirectory.PageAllAsync(unit, WorkDispatcher.Module, context.Cursor ?? "", context.Batch, cancellationToken);
            }

            foreach (var listed in page)
            {
                if (!DirectoryScopeResolver.TryResolve(listed, out var scope))
                {
                    WorkLog.DirectoryRowMalformed(_logger, WorkDispatcher.Module);
                    continue;
                }

                while (await DeleteAsync(scope, context.Batch, cancellationToken) >= context.Batch)
                {
                }

                await WorkDispatcher.SettleAsync(_units, scope, cancellationToken);
            }

            if (page.Count < context.Batch)
            {
                return RecurringJobOutcome.Completed;
            }

            await context.SaveCursorAsync(page[^1], cancellationToken);
        }
    }

    private async Task<int> DeleteAsync(UnitOfWorkScope scope, int batch, CancellationToken cancellationToken)
    {
        await using var unit = await _units.OpenAsync(scope, new UnitOfWorkOptions(ShardLeases.Module, "work.retention_delete"), cancellationToken);
        var deleted = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, DeleteSql)
            .With(_settings.CompletedRetention).With(_settings.PoisonedRetention).With((long)batch).ExecuteAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return deleted;
    }
}

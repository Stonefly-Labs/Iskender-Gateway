using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The shipped job <c>coordination.purge_expired</c> (<c>work</c> contract Recurring jobs rule 4, FR-020, research R10):
/// it pages the scope directory's <c>coordination</c> rows that are due, in an operator unit, and for each scope calls
/// <see cref="CoordinationMaintenance.PurgeExpiredAsync"/> until a call deletes fewer than the batch, then
/// <see cref="CoordinationMaintenance.SettleDirectoryAsync"/>. The deletes run in the maintenance's own units, unfenced,
/// and never touch a fence record or a namespace generation. It saves the last scope of each page as its cursor, so a
/// crash resumes after it; a repeated scope only finds its rows gone. A malformed row is skipped with event 8009. Owned by
/// ISK-0009.
/// </summary>
internal sealed class PurgeExpiredJob : IRecurringJob
{
    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationMaintenance _maintenance;
    private readonly ILogger _logger;

    public PurgeExpiredJob(IUnitOfWorkFactory units, CoordinationSettings settings, ILoggerFactory? loggers = null)
    {
        _units = units;
        _logger = loggers?.CreateLogger(WorkLog.Category) ?? NullLogger.Instance;
        _maintenance = new CoordinationMaintenance(units, settings, loggers?.CreateLogger(CoordinationTelemetry.SourceName));
    }

    /// <summary>The registration <c>AddIskenderPostgreSqlWorkDispatcher()</c> adds: every 5 min, 500 at a time, by default.</summary>
    public static RecurringJobRegistration Registration => new(WorkSettings.PurgeExpiredJobName, TimeSpan.FromMinutes(5), 500, typeof(PurgeExpiredJob));

    public string Name => WorkSettings.PurgeExpiredJobName;

    public async ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<string> page;
            await using (var unit = await _units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions(ShardLeases.Module, "work.purge_page"), cancellationToken))
            {
                page = await ScopeDirectory.PageDueAsync(unit, CoordinationDirectory.Module, context.Cursor ?? "", context.Batch, cancellationToken);
            }

            foreach (var listed in page)
            {
                if (!DirectoryScopeResolver.TryResolve(listed, out var scope))
                {
                    WorkLog.DirectoryRowMalformed(_logger, CoordinationDirectory.Module);
                    continue;
                }

                var coordinationScope = CoordinationScope.From(scope);
                while (await _maintenance.PurgeExpiredAsync(coordinationScope, context.Batch, cancellationToken) >= context.Batch)
                {
                }

                await _maintenance.SettleDirectoryAsync(coordinationScope, cancellationToken);
            }

            if (page.Count < context.Batch)
            {
                return RecurringJobOutcome.Completed;
            }

            await context.SaveCursorAsync(page[^1], cancellationToken);
        }
    }
}

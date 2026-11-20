using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// One registered job as the runner runs it: its registration, the interval and batch it runs with, and the leases and
/// fencing guard of its own feature, <c>CoordinationGate.ForFeature("work.job.&lt;name&gt;")</c>. Owned by ISK-0009.
/// </summary>
internal sealed record ScheduledJob(RecurringJobRegistration Registration, TimeSpan Interval, int Batch, ILeaseCoordinator Leases, IFencingGuard Fencing)
{
    public string Name => Registration.Name;

    /// <summary>The job's operator-scope lease key and fence resource: <c>work</c> / <c>job.&lt;name&gt;</c>.</summary>
    public CoordinationName Resource { get; } = new(ShardLeases.Module, "job." + Registration.Name);

    /// <summary>The job's feature: <c>work.job.&lt;name&gt;</c>.</summary>
    public static string Feature(string name) => "work.job." + name;
}

/// <summary>
/// Runs the recurring jobs (<c>work</c> contract Recurring jobs rules 1 to 3, research R10). Every
/// <c>pollInterval</c> it checks each job: once <c>last_started_at + interval</c> has passed on the database clock, it
/// tries the job's lease <c>TryOnly</c>; a lease held elsewhere, or granted with nothing remaining, writes 8011
/// <c>held</c>. Holding it, an operator unit
/// passes the fencing guard for the job's resource and records <c>last_started_at</c>, checking again that the job is
/// due, so a run that another worker started in the meantime is not repeated. The job runs from its saved cursor, and
/// each cursor it saves, and the finish that records <c>last_finished_at</c>, is the same fenced write. A
/// <see cref="RecurringJobOutcome.Completed"/> run clears the cursor and a <see cref="RecurringJobOutcome.Partial"/> one
/// keeps it. Each run writes 8011, <c>iskender.work.job.runs</c>, and the <c>work.job</c> activity, and a check that fails
/// before any run writes 8012 once per change of class and code, across all the jobs, since 8012 names no job. A start
/// that the stop cuts short is not a run and reports nothing. No advisory lock is used. Owned by ISK-0009.
/// </summary>
internal sealed class JobRunner
{
    private const string DueSql =
        "SELECT last_started_at IS NULL OR last_started_at + $3::interval <= clock_timestamp() FROM isk_work.job_runs WHERE {scope} AND job = $2";

    private const string StartSql =
        "INSERT INTO isk_work.job_runs AS r (scope_key, job, last_started_at) VALUES ($1, $2, clock_timestamp()) "
        + "ON CONFLICT (scope_key, job) DO UPDATE SET last_started_at = clock_timestamp() "
        + "WHERE r.last_started_at IS NULL OR r.last_started_at + $3::interval <= clock_timestamp() "
        + "RETURNING r.cursor";

    private const string CursorSql = "UPDATE isk_work.job_runs SET cursor = $3 WHERE {scope} AND job = $2";

    private const string FinishSql =
        "UPDATE isk_work.job_runs SET last_finished_at = clock_timestamp(), cursor = CASE WHEN $3 THEN NULL ELSE cursor END WHERE {scope} AND job = $2";

    private readonly IUnitOfWorkFactory _units;
    private readonly IReadOnlyList<ScheduledJob> _jobs;
    private readonly Func<Type, IRecurringJob> _resolve;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _leaseDuration;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private WorkFailure? _lastFailure;

    public JobRunner(
        IUnitOfWorkFactory units,
        IEnumerable<ScheduledJob> jobs,
        Func<Type, IRecurringJob> resolve,
        TimeSpan pollInterval,
        TimeSpan leaseDuration,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _jobs = jobs?.ToList() ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _resolve = resolve ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _pollInterval = pollInterval;
        _leaseDuration = leaseDuration;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Checks every job each <c>pollInterval</c> until <paramref name="stop"/> fires, which also cancels a run.</summary>
    public Task RunAsync(CancellationToken stop) => RunAsync(stop, stop, stop);

    /// <summary>
    /// Checks every job each <c>pollInterval</c> until <paramref name="stop"/> fires, which starts no further run. A run
    /// already started continues until <paramref name="cancelRuns"/> fires, at the stop deadline, and its finish write and
    /// lease release until <paramref name="abandonRecords"/> fires, 2 seconds after it (Shutdown rules 1 to 3).
    /// </summary>
    public async Task RunAsync(CancellationToken stop, CancellationToken cancelRuns, CancellationToken abandonRecords)
    {
        while (!stop.IsCancellationRequested)
        {
            foreach (var job in _jobs)
            {
                try
                {
                    await CheckAsync(job, stop, cancelRuns, abandonRecords);
                    _lastFailure = null;
                }
                catch (Exception) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The check or the lease acquire failed, such as on an unreachable database, before any run began: 8012,
                    // once per change of class and code across all the jobs, since 8012 names no job, and the next poll tries
                    // again (Diagnostics rule 2).
                    var failure = WorkClassifier.Classify(exception);
                    if (_lastFailure != failure)
                    {
                        _lastFailure = failure;
                        WorkLog.PassFailed(_logger, failure.Class, failure.Code);
                    }
                }
            }

            try
            {
                await Task.Delay(_pollInterval, _timeProvider, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Checks <paramref name="job"/> once and runs it when it is due, returning the run's outcome, <c>held</c> when another
    /// worker holds its lease, or <c>null</c> when it is not due.
    /// </summary>
    public Task<string?> CheckAsync(ScheduledJob job, CancellationToken cancellationToken) => CheckAsync(job, cancellationToken, cancellationToken, cancellationToken);

    // cancellationToken ends the check before a run starts; cancelRun ends a started run; abandonRecords ends its finish
    // write and its lease release, as it does a shard's failure records and lease releases.
    private async Task<string?> CheckAsync(ScheduledJob job, CancellationToken cancellationToken, CancellationToken cancelRun, CancellationToken abandonRecords)
    {
        if (!await DueAsync(job, cancellationToken))
        {
            return null;
        }

        var acquisition = await job.Leases.AcquireAsync(CoordinationScope.From(UnitOfWorkScope.Operator), job.Resource, _leaseDuration, LeaseWait.TryOnly, cancellationToken);
        if (acquisition.Lease is not { } granted)
        {
            Report(job, WorkOutcomes.Held);
            return WorkOutcomes.Held;
        }

        await using var lease = new ShardLease(job.Leases, granted, job.Resource, _leaseDuration, _timeProvider);
        if (!lease.IsSafeToAct)
        {
            // An acquire that waited past its lease's expiry is granted with nothing remaining (Leases rule 3). Like a lease
            // held elsewhere, it starts no run, so it is released and reported as held.
            await lease.ReleaseAsync(abandonRecords);
            Report(job, WorkOutcomes.Held);
            return WorkOutcomes.Held;
        }

        Activity? activity = null;
        var outcome = WorkOutcomes.Failed;
        var ran = true;
        try
        {
            var started = await StartAsync(job, lease, cancellationToken);
            if (!started.Due)
            {
                ran = false;
                return null;
            }

            // Started only once the guarded start finds the job due, so a start that is not a run exports no span (Tracing rule 3).
            activity = WorkTelemetry.StartJob(job.Name);
            using var run = CancellationTokenSource.CreateLinkedTokenSource(cancelRun, lease.Lost);
            var context = new RecurringJobContext(job.Batch, started.Cursor, (cursor, token) => WriteAsync(job, lease, CursorSql, cursor, token));
            var result = await _resolve(job.Registration.JobType).RunAsync(context, run.Token);
            await WriteAsync(job, lease, FinishSql, result == RecurringJobOutcome.Completed, abandonRecords);
            outcome = result == RecurringJobOutcome.Completed ? WorkOutcomes.Completed : WorkOutcomes.Partial;
            return outcome;
        }
        catch (Exception) when (activity is null && cancellationToken.IsCancellationRequested)
        {
            // The stop cut the guarded start short, so no run began: no span, 8011, or metric (Tracing rule 3, Shutdown rule 1).
            ran = false;
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !(cancellationToken.IsCancellationRequested || cancelRun.IsCancellationRequested))
        {
            return outcome;
        }
        finally
        {
            if (ran)
            {
                // A start that failed is reported as a failed run, with its span, as before the start was guarded.
                WorkTelemetry.End(activity ?? WorkTelemetry.StartJob(job.Name), outcome);
                Report(job, outcome);
            }

            await lease.ReleaseAsync(abandonRecords);
        }
    }

    private async Task<bool> DueAsync(ScheduledJob job, CancellationToken cancellationToken)
    {
        await using var unit = await OpenAsync("work.job_check", cancellationToken);
        var rows = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.JobRuns, DueSql).With(job.Name).With(job.Interval)
            .QueryAsync(reader => reader.GetBoolean(0), cancellationToken);
        return rows.Count == 0 || rows[0];
    }

    // Records the start through the guard, if the job is still due, and returns the cursor to resume from.
    private async Task<(bool Due, string? Cursor)> StartAsync(ScheduledJob job, ShardLease lease, CancellationToken cancellationToken)
    {
        await using var unit = await OpenAsync("work.job_start", cancellationToken);
        await job.Fencing.AcceptAsync(unit, job.Resource, lease.Current, cancellationToken);
        var rows = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.JobRuns, StartSql).With(job.Name).With(job.Interval)
            .QueryAsync(reader => reader.IsDBNull(0) ? null : reader.GetString(0), cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return rows.Count == 0 ? (false, null) : (true, rows[0]);
    }

    // A bookkeeping write, a cursor or the finish, through the guard in its own operator unit.
    private async ValueTask WriteAsync<T>(ScheduledJob job, ShardLease lease, string sql, T value, CancellationToken cancellationToken)
    {
        await using var unit = await OpenAsync("work.job_record", cancellationToken);
        await job.Fencing.AcceptAsync(unit, job.Resource, lease.Current, cancellationToken);
        await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.JobRuns, sql).With(job.Name).With(value).ExecuteAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    private void Report(ScheduledJob job, string outcome)
    {
        WorkLog.JobRun(_logger, job.Name, outcome);
        WorkTelemetry.RecordJobRun(job.Name, outcome);
    }

    private ValueTask<IUnitOfWork> OpenAsync(string operation, CancellationToken cancellationToken) =>
        _units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions(ShardLeases.Module, operation), cancellationToken);
}

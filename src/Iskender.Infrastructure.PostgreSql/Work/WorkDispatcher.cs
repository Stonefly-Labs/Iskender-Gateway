using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The dispatch cycle of research R6 (<c>work</c> contract Dispatch and apply, Retry and poison, Version skew): page the
/// directory's due <c>work</c> rows in an operator unit, turn each row into a scope only through
/// <see cref="DirectoryScopeResolver"/>, count and claim each leased shard's due handled items in a unit of that scope,
/// and run each item as an attempt record, an apply, and, when the handler fails, a failure record, each its own fenced
/// unit at Read Committed and conditional on the item being <c>pending</c> with the attempt count the claim read. Every
/// item read and write happens in its own scope's unit. Owned by ISK-0009.
/// </summary>
internal sealed class WorkDispatcher : IDisposable
{
    /// <summary>The unit module and the directory module of the dispatcher.</summary>
    public const string Module = "work";

    private static readonly string CountsSql =
        "SELECT bucket % $2, kind, " + Handled("$3", "$4", "$5") + ", count(*) FROM isk_work.items i "
        + "WHERE {scope} AND state = 'pending' AND due_at <= clock_timestamp() GROUP BY 1, 2, 3";

    private static readonly string ClaimSql =
        "SELECT item_id, kind, payload_version, payload, idempotency_key, attempt_count, last_failure_class, last_failure_code, trace_parent, trace_state "
        + "FROM isk_work.items i WHERE {scope} AND state = 'pending' AND due_at <= clock_timestamp() AND bucket % $2 = $3 AND "
        + Handled("$4", "$5", "$6") + " ORDER BY due_at, item_id LIMIT $7";

    private const string PoisonAtClaimSql =
        "UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3";

    private const string AttemptSql =
        "UPDATE isk_work.items SET attempt_count = attempt_count + 1, due_at = clock_timestamp() + $4::interval, "
        + "last_failure_class = 'abandoned', last_failure_code = 'work.attempt_abandoned' "
        + "WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3";

    private const string LockSql = "SELECT 1 FROM isk_work.items WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3 FOR UPDATE";

    private const string CompleteSql =
        "UPDATE isk_work.items SET state = 'completed', finished_at = clock_timestamp(), last_failure_class = NULL, last_failure_code = NULL "
        + "WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3";

    private const string FailureSql =
        "UPDATE isk_work.items SET last_failure_class = $4, last_failure_code = $5, "
        + "state = CASE WHEN $6 THEN 'poisoned' ELSE state END, finished_at = CASE WHEN $6 THEN clock_timestamp() ELSE finished_at END "
        + "WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3";

    private const string RemainingSql =
        "SELECT EXISTS (SELECT 1 FROM isk_work.items WHERE {scope}), (SELECT min(due_at) FROM isk_work.items WHERE {scope} AND state = 'pending')";

    private readonly IUnitOfWorkFactory _units;
    private readonly IFencingGuard _fencing;
    private readonly ShardLeases _leases;
    private readonly WorkSettings _settings;
    private readonly Dictionary<WorkKind, WorkHandlerRegistration> _handlers;
    private readonly Func<Type, IWorkHandler> _resolve;
    private readonly IWorkInjection _injection;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _random;
    private readonly string[] _kinds;
    private readonly int[] _lowest;
    private readonly int[] _highest;
    private readonly SemaphoreSlim _workerSlots;
    private readonly HashSet<string> _skewedKinds = new(StringComparer.Ordinal);
    private readonly object _passFailureGate = new();
    private string _cursor = "";
    private WorkFailure? _lastPassFailure;

    public WorkDispatcher(
        IUnitOfWorkFactory units,
        IFencingGuard fencing,
        ShardLeases leases,
        WorkSettings settings,
        IEnumerable<WorkHandlerRegistration> handlers,
        Func<Type, IWorkHandler> resolve,
        IWorkInjection? injection,
        ILogger? logger,
        TimeProvider? timeProvider,
        Func<double>? random = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _fencing = fencing ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _leases = leases ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _resolve = resolve ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _handlers = (handlers ?? throw new GatewayException(ErrorCodes.ValueInvalid)).ToDictionary(handler => handler.Kind);
        _injection = injection ?? NoWorkInjection.Instance;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? Random.Shared.NextDouble;
        var ordered = _handlers.Values.OrderBy(handler => handler.Kind.ToString(), StringComparer.Ordinal).ToList();
        _kinds = [.. ordered.Select(handler => handler.Kind.ToString())];
        _lowest = [.. ordered.Select(handler => handler.LowestPayloadVersion)];
        _highest = [.. ordered.Select(handler => handler.HighestPayloadVersion)];
        _workerSlots = new SemaphoreSlim(settings.Concurrency, settings.Concurrency);
    }

    public void Dispose() => _workerSlots.Dispose();

    /// <summary>Items running in this worker now, for the cap tests.</summary>
    public int InFlight => _settings.Concurrency - _workerSlots.CurrentCount;

    /// <summary>
    /// Runs passes until <paramref name="stopClaiming"/> fires, waking every <c>pollInterval</c> and at once after a pass
    /// that claimed a full batch, then waits for the in-flight items. <paramref name="cancelHandlers"/> cancels the running
    /// handlers, whose attempts are then recorded as <c>work.handler_cancelled</c>, and <paramref name="abandonRecords"/>
    /// abandons the failure records and releases still running (<c>work</c> contract Shutdown rules 1 to 3).
    /// </summary>
    public async Task RunAsync(CancellationToken stopClaiming, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        while (!stopClaiming.IsCancellationRequested)
        {
            var full = false;
            try
            {
                full = await PassAsync(stopClaiming, cancelHandlers, abandonRecords);
                PassSucceeded();
            }
            catch (OperationCanceledException) when (stopClaiming.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PassFailed(exception);
            }

            if (full)
            {
                continue;
            }

            try
            {
                await Task.Delay(_settings.PollInterval, _timeProvider, stopClaiming);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One pass over one page of the directory. Returns whether any shard's claim was a full batch, so the next pass
    /// starts at once.
    /// </summary>
    public async Task<bool> PassAsync(CancellationToken stopClaiming, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        IReadOnlyList<string> page;
        await using (var unit = await _units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions(Module, "work.directory_page"), stopClaiming))
        {
            page = await ScopeDirectory.PageDueAsync(unit, Module, _cursor, _settings.BatchSize, stopClaiming);
            await unit.CommitAsync(stopClaiming);
        }

        _cursor = page.Count < _settings.BatchSize ? "" : page[^1];
        var skewed = new Dictionary<string, long>(StringComparer.Ordinal);
        var backlog = new Dictionary<string, long>(StringComparer.Ordinal);
        var full = false;
        foreach (var listed in page)
        {
            if (stopClaiming.IsCancellationRequested)
            {
                break;
            }

            if (!DirectoryScopeResolver.TryResolve(listed, out var scope))
            {
                WorkLog.DirectoryRowMalformed(_logger, Module);
                continue;
            }

            full |= await ScopeAsync(scope, skewed, backlog, stopClaiming, cancelHandlers, abandonRecords);
        }

        ReportGauges(skewed, backlog);
        return full;
    }

    private async Task<bool> ScopeAsync(
        UnitOfWorkScope scope,
        Dictionary<string, long> skewed,
        Dictionary<string, long> backlog,
        CancellationToken stopClaiming,
        CancellationToken cancelHandlers,
        CancellationToken abandonRecords)
    {
        var shards = new Dictionary<int, long>();
        await using (var unit = await OpenAsync(scope, "work.shard_counts", stopClaiming))
        {
            var rows = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, CountsSql)
                .With(_settings.Shards).With(_kinds).With(_lowest).With(_highest)
                .QueryAsync(reader => (Shard: reader.GetInt32(0), Kind: reader.GetString(1), Handled: reader.GetBoolean(2), Count: reader.GetInt64(3)), stopClaiming);
            await unit.CommitAsync(stopClaiming);
            foreach (var row in rows)
            {
                var totals = row.Handled ? backlog : skewed;
                totals[row.Kind] = totals.GetValueOrDefault(row.Kind) + row.Count;
                if (row.Handled)
                {
                    shards[row.Shard] = shards.GetValueOrDefault(row.Shard) + row.Count;
                }
            }
        }

        // A shard's lease is tried only within the in-flight caps (research R6 step 3): one worker slot and one scope slot
        // are reserved first, for the shard's first item. A contended shard hands its reservation to the next shard, and
        // the remaining shards are left to other workers or the next pass once no slot is free.
        using var scopeSlots = new SemaphoreSlim(_settings.ScopeConcurrency, _settings.ScopeConcurrency);
        var passes = new List<Task<bool>>();
        try
        {
            foreach (var shard in shards.Keys.Order())
            {
                if (stopClaiming.IsCancellationRequested || !scopeSlots.Wait(0))
                {
                    break;
                }

                if (!_workerSlots.Wait(0))
                {
                    scopeSlots.Release();
                    break;
                }

                ShardLease? lease;
                try
                {
                    lease = await _leases.TryAcquireAsync(scope, shard, stopClaiming);
                }
                catch
                {
                    _workerSlots.Release();
                    scopeSlots.Release();
                    throw;
                }

                if (lease is null)
                {
                    _workerSlots.Release();
                    scopeSlots.Release();
                    continue;
                }

                passes.Add(ShardAsync(scope, shard, lease, scopeSlots, stopClaiming, cancelHandlers, abandonRecords));
            }
        }
        catch (OperationCanceledException) when (stopClaiming.IsCancellationRequested)
        {
            // The shards already taken finish below.
        }

        var results = await Task.WhenAll(passes);

        if (!stopClaiming.IsCancellationRequested)
        {
            await SettleAsync(scope, stopClaiming);
        }

        return results.Any(result => result);
    }

    // Holds the shard's lease and one reserved worker slot and scope slot on entry, which the shard's first item takes over.
    private async Task<bool> ShardAsync(
        UnitOfWorkScope scope,
        int shard,
        ShardLease lease,
        SemaphoreSlim scopeSlots,
        CancellationToken stopClaiming,
        CancellationToken cancelHandlers,
        CancellationToken abandonRecords)
    {
        var reserved = true;
        var claimed = new List<ClaimedItem>();
        var items = new List<Task>();
        try
        {
            await using (var unit = await OpenAsync(scope, "work.claim", stopClaiming))
            {
                claimed.AddRange(await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, ClaimSql)
                    .With(_settings.Shards).With(shard).With(_kinds).With(_lowest).With(_highest).With(_settings.BatchSize)
                    .QueryAsync(ClaimedItem.Read, stopClaiming));
                await unit.CommitAsync(stopClaiming);
            }

            foreach (var item in claimed)
            {
                if (stopClaiming.IsCancellationRequested || !lease.IsSafeToAct)
                {
                    break;
                }

                if (reserved)
                {
                    reserved = false;
                }
                else
                {
                    await scopeSlots.WaitAsync(stopClaiming);
                    try
                    {
                        await _workerSlots.WaitAsync(stopClaiming);
                    }
                    catch
                    {
                        scopeSlots.Release();
                        throw;
                    }
                }

                items.Add(RunItemAsync(scope, lease, item, scopeSlots, cancelHandlers, abandonRecords));
            }
        }
        catch (OperationCanceledException) when (stopClaiming.IsCancellationRequested)
        {
            // No further item is claimed or started; the started ones finish below.
        }
        finally
        {
            if (reserved)
            {
                _workerSlots.Release();
                scopeSlots.Release();
            }

            await Task.WhenAll(items);
            await lease.ReleaseAsync(abandonRecords);
            await lease.DisposeAsync();
        }

        return claimed.Count == _settings.BatchSize;
    }

    private async Task RunItemAsync(UnitOfWorkScope scope, ShardLease lease, ClaimedItem item, SemaphoreSlim scopeSlots, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        await Task.Yield();
        try
        {
            await AttemptAsync(scope, lease, item, cancelHandlers, abandonRecords);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An item's units failed outside the handler, such as the database becoming unreachable. Nothing more is
            // written for it; the attempt record, if it committed, keeps the attempt counted (Retry and poison rule 7).
            PassFailed(exception);
        }
        finally
        {
            _workerSlots.Release();
            scopeSlots.Release();
        }
    }

    private async Task AttemptAsync(UnitOfWorkScope scope, ShardLease lease, ClaimedItem item, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        var registration = _handlers[item.Kind];
        var maximum = registration.MaximumAttempts ?? _settings.MaximumAttempts;
        var started = _timeProvider.GetTimestamp();
        WorkTelemetry.RecordDispatched(item.Kind);

        // The attempt record (Dispatch and apply rule 4), or the poison at the claim of Retry and poison rule 4.
        await using (var unit = await OpenAsync(scope, "work.attempt", cancelHandlers))
        {
            if (!await AcceptAsync(unit, lease, item, item.AttemptCount, cancelHandlers))
            {
                return;
            }

            var adapter = PostgreSqlUnitOfWork.From(unit);
            if (item.AttemptCount >= maximum)
            {
                if (await adapter.Command(WorkTables.Items, PoisonAtClaimSql).With(item.ItemId).With(item.AttemptCount).ExecuteAsync(cancelHandlers) == 0)
                {
                    return;
                }

                await unit.CommitAsync(cancelHandlers);
                var last = item.LastFailure ?? WorkClassifier.Abandoned;
                WorkLog.ItemPoisoned(_logger, item.Kind, item.AttemptCount, last);
                WorkTelemetry.RecordPoisoned(item.Kind, last);
                WorkTelemetry.RecordDispatchDuration(item.Kind, "poisoned", _timeProvider.GetElapsedTime(started));
                return;
            }

            var backoff = WorkBackoff.Delay(item.AttemptCount + 1, _settings.InitialDelay, _settings.MaximumDelay, _random);
            if (await adapter.Command(WorkTables.Items, AttemptSql).With(item.ItemId).With(item.AttemptCount).With(backoff).ExecuteAsync(cancelHandlers) == 0)
            {
                return;
            }

            await _injection.ReachedAsync(WorkInjectionPoint.BeforeAttemptCommit, item.ItemId, cancelHandlers);
            await unit.CommitAsync(cancelHandlers);
        }

        var attempt = item.AttemptCount + 1;
        await _injection.ReachedAsync(WorkInjectionPoint.AfterAttemptCommit, item.ItemId, cancelHandlers);

        // The apply (Dispatch and apply rule 5).
        var failure = await ApplyAsync(scope, lease, item, registration, attempt, started, cancelHandlers);
        if (failure is not { } recorded)
        {
            return;
        }

        // The failure record (Dispatch and apply rule 6).
        var poison = recorded.Class == WorkFailureClass.Permanent || attempt >= maximum;
        await using (var unit = await OpenAsync(scope, "work.failure", abandonRecords))
        {
            if (!await AcceptAsync(unit, lease, item, attempt, abandonRecords))
            {
                return;
            }

            if (await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, FailureSql)
                .With(item.ItemId).With(attempt).With(recorded.Class.ToCode()).With(recorded.Code.Id).With(poison)
                .ExecuteAsync(abandonRecords) == 0)
            {
                return;
            }

            await _injection.ReachedAsync(WorkInjectionPoint.BeforeFailureCommit, item.ItemId, abandonRecords);
            await unit.CommitAsync(abandonRecords);
        }

        await _injection.ReachedAsync(WorkInjectionPoint.AfterFailureCommit, item.ItemId, abandonRecords);
        if (poison)
        {
            WorkLog.ItemPoisoned(_logger, item.Kind, attempt, recorded);
            WorkTelemetry.RecordPoisoned(item.Kind, recorded);
            WorkTelemetry.RecordDispatchDuration(item.Kind, "poisoned", _timeProvider.GetElapsedTime(started));
        }
        else
        {
            WorkLog.RetryScheduled(_logger, item.Kind, attempt, recorded);
            WorkTelemetry.RecordRetried(item.Kind, recorded);
            WorkTelemetry.RecordDispatchDuration(item.Kind, "retried", _timeProvider.GetElapsedTime(started));
        }
    }

    // Runs the handler in one fenced unit that holds the item's row lock. Returns the failure to record, or null when the
    // apply committed or nothing more may be written for the item.
    private async Task<WorkFailure?> ApplyAsync(UnitOfWorkScope scope, ShardLease lease, ClaimedItem item, WorkHandlerRegistration registration, int attempt, long started, CancellationToken cancelHandlers)
    {
        using var timeout = new CancellationTokenSource(registration.Timeout ?? _settings.HandlerTimeout, _timeProvider);
        using var handlerToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancelHandlers, lease.Lost);
        var activity = WorkTelemetry.StartDispatch(item.Kind, attempt, item.RecordedContext);
        try
        {
            await using var unit = await OpenAsync(scope, "work.apply", handlerToken.Token);
            if (!await AcceptAsync(unit, lease, item, attempt, handlerToken.Token))
            {
                WorkTelemetry.End(activity, WorkOutcomes.FencingRejected);
                return null;
            }

            var adapter = PostgreSqlUnitOfWork.From(unit);
            if ((await adapter.Command(WorkTables.Items, LockSql).With(item.ItemId).With(attempt).QueryAsync(_ => true, handlerToken.Token)).Count == 0)
            {
                WorkLog.ApplyDiscarded(_logger, item.Kind, attempt, WorkOutcomes.Superseded);
                WorkTelemetry.End(activity, WorkOutcomes.Superseded);
                return null;
            }

            var delivery = new WorkDelivery(item.ItemId, scope.Owner, item.Kind, item.PayloadVersion, item.Payload, item.IdempotencyKey, attempt);
            await _resolve(registration.HandlerType).HandleAsync(delivery, unit, handlerToken.Token);
            await _injection.ReachedAsync(WorkInjectionPoint.DuringHandler, item.ItemId, handlerToken.Token);
            if (lease.IsLost)
            {
                throw new OperationCanceledException(lease.Lost);
            }

            await adapter.Command(WorkTables.Items, CompleteSql).With(item.ItemId).With(attempt).ExecuteAsync(handlerToken.Token);
            await unit.CommitAsync(handlerToken.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (lease.IsLost)
            {
                WorkLog.ApplyDiscarded(_logger, item.Kind, attempt, WorkOutcomes.LeaseLost);
                WorkTelemetry.End(activity, WorkOutcomes.LeaseLost);
                return null;
            }

            if (exception is GatewayException { Code: var code } && ReferenceEquals(code, ErrorCodes.CoordinationFencingRejected))
            {
                WorkLog.ApplyDiscarded(_logger, item.Kind, attempt, WorkOutcomes.FencingRejected);
                WorkTelemetry.End(activity, WorkOutcomes.FencingRejected);
                return null;
            }

            var failure = exception is OperationCanceledException && timeout.IsCancellationRequested && !cancelHandlers.IsCancellationRequested
                ? WorkClassifier.Timeout
                : exception is OperationCanceledException && cancelHandlers.IsCancellationRequested
                    ? WorkClassifier.Shutdown
                    : WorkClassifier.Classify(exception);
            WorkTelemetry.End(activity, WorkOutcomes.Failed);
            return failure;
        }

        await _injection.ReachedAsync(WorkInjectionPoint.AfterApplyCommit, item.ItemId, CancellationToken.None);
        WorkLog.ItemCompleted(_logger, item.Kind, attempt);
        WorkTelemetry.RecordCompleted(item.Kind);
        WorkTelemetry.RecordDispatchDuration(item.Kind, WorkOutcomes.Completed, _timeProvider.GetElapsedTime(started));
        WorkTelemetry.End(activity, WorkOutcomes.Completed);
        return null;
    }

    // Accepts the shard lease's token for the shard's fence resource in unit. A rejection commits nothing and writes 8010.
    // A lost lease writes nothing more (Dispatch and apply rule 3); otherwise the guard decides, even for a lease whose
    // local remaining time has run out, because only the guard knows whether a newer holder has written.
    private async Task<bool> AcceptAsync(IUnitOfWork unit, ShardLease lease, ClaimedItem item, int attempt, CancellationToken cancellationToken)
    {
        if (lease.IsLost)
        {
            WorkLog.ApplyDiscarded(_logger, item.Kind, Math.Max(attempt, 1), WorkOutcomes.LeaseLost);
            return false;
        }

        try
        {
            await _fencing.AcceptAsync(unit, lease.Resource, lease.Current, cancellationToken);
            return true;
        }
        catch (GatewayException failure) when (ReferenceEquals(failure.Code, ErrorCodes.CoordinationFencingRejected))
        {
            WorkLog.ApplyDiscarded(_logger, item.Kind, Math.Max(attempt, 1), WorkOutcomes.FencingRejected);
            return false;
        }
    }

    // Settles the scope's work row: kept with the earliest pending due time while any item remains, else removed.
    private async Task SettleAsync(UnitOfWorkScope scope, CancellationToken cancellationToken)
    {
        await using var unit = await OpenAsync(scope, "work.settle", cancellationToken);
        await ScopeDirectory.SettleAsync(
            unit,
            Module,
            async token =>
            {
                var rows = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, RemainingSql)
                    .QueryAsync(reader => (Any: reader.GetBoolean(0), Due: reader.IsDBNull(1) ? (DateTime?)null : reader.GetFieldValue<DateTime>(1)), token);
                return rows[0].Any ? DirectoryRemaining.Due(rows[0].Due) : DirectoryRemaining.None;
            },
            cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    // The kinds and payload versions this worker handles: research R8's filter over $kinds, $lowest, and $highest.
    private static string Handled(string kinds, string lowest, string highest) =>
        $"EXISTS (SELECT 1 FROM unnest({kinds}::text[], {lowest}::integer[], {highest}::integer[]) h(kind, lowest, highest) "
        + "WHERE h.kind = i.kind AND i.payload_version BETWEEN h.lowest AND h.highest)";

    private ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, string operation, CancellationToken cancellationToken) =>
        _units.OpenAsync(scope, new UnitOfWorkOptions(Module, operation), cancellationToken);

    // The skewed and backlog gauges as of this pass, and event 8008 when a kind's skew appears or clears in the process.
    private void ReportGauges(Dictionary<string, long> skewed, Dictionary<string, long> backlog)
    {
        foreach (var kind in _skewedKinds.Except(skewed.Keys, StringComparer.Ordinal).ToList())
        {
            _skewedKinds.Remove(kind);
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                WorkTelemetry.SetSkewed(parsed, 0);
                WorkLog.ItemsSkewed(_logger, parsed, appeared: false);
            }
        }

        foreach (var (kind, count) in skewed)
        {
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                WorkTelemetry.SetSkewed(parsed, count);
                if (_skewedKinds.Add(kind))
                {
                    WorkLog.ItemsSkewed(_logger, parsed, appeared: true);
                }
            }
        }

        foreach (var kind in _kinds)
        {
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                WorkTelemetry.SetBacklog(parsed, backlog.GetValueOrDefault(kind));
            }
        }
    }

    // Event 8012, once per change of class and code; a pass that succeeds lets the next failure be written again.
    private void PassFailed(Exception exception)
    {
        var failure = WorkClassifier.Classify(exception);
        lock (_passFailureGate)
        {
            if (_lastPassFailure == failure)
            {
                return;
            }

            _lastPassFailure = failure;
        }

        WorkLog.PassFailed(_logger, failure.Class, failure.Code);
    }

    private void PassSucceeded()
    {
        lock (_passFailureGate)
        {
            _lastPassFailure = null;
        }
    }

    // One claimed item, as the claim read it.
    private sealed record ClaimedItem(
        Guid ItemId,
        WorkKind Kind,
        int PayloadVersion,
        byte[] Payload,
        string IdempotencyKey,
        int AttemptCount,
        WorkFailure? LastFailure,
        ActivityContext? RecordedContext)
    {
        public static ClaimedItem Read(Npgsql.NpgsqlDataReader reader)
        {
            if (!WorkKind.TryParse(reader.GetString(1), out var kind) || kind is null)
            {
                throw new GatewayException(ErrorCodes.Internal);
            }

            WorkFailure? last = null;
            if (!reader.IsDBNull(6) && WorkFailureClasses.TryParse(reader.GetString(6), out var failureClass)
                && ErrorCodes.All.FirstOrDefault(code => code.Id == (reader.IsDBNull(7) ? null : reader.GetString(7))) is { } code)
            {
                last = new WorkFailure(failureClass, code);
            }

            ActivityContext? recorded = null;
            if (!reader.IsDBNull(8) && ActivityContext.TryParse(reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), isRemote: true, out var context))
            {
                recorded = context;
            }

            return new ClaimedItem(reader.GetGuid(0), kind, reader.GetInt32(2), (byte[])reader.GetValue(3), reader.GetString(4), reader.GetInt32(5), last, recorded);
        }
    }
}

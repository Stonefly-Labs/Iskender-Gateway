using System.Diagnostics;
using System.Runtime.ExceptionServices;
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

    // With $7, a cancellation at shutdown, the attempt is handed back: the count drops by the one its attempt record added.
    private const string FailureSql =
        "UPDATE isk_work.items SET last_failure_class = $4, last_failure_code = $5, attempt_count = attempt_count - CASE WHEN $7 THEN 1 ELSE 0 END, "
        + "state = CASE WHEN $6 THEN 'poisoned' ELSE state END, finished_at = CASE WHEN $6 THEN clock_timestamp() ELSE finished_at END "
        + "WHERE {scope} AND item_id = $2 AND state = 'pending' AND attempt_count = $3";

    // How much longer than the handler's timeout the apply unit may stay open, for its accept, lock, completion, and
    // commit, so the handler's timeout, not the unit's, ends a handler that runs too long (Dispatch and apply rule 8).
    private static readonly TimeSpan ApplyMargin = TimeSpan.FromSeconds(30);

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
    private readonly WorkGauges _gauges = new();
    private readonly object _passFailureGate = new();

    // Guards the shards this worker runs, each scope's used slots, the running batches, and the scopes' shard offsets.
    private readonly object _shardGate = new();
    private readonly HashSet<(string Scope, int Shard)> _held = [];
    private readonly Dictionary<string, int> _scopeSlots = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _running = [];
    private Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
    private Dictionary<string, int> _lastOffsets = new(StringComparer.Ordinal);
    private string _cursor = "";
    private Dictionary<string, long> _cycleSkewed = new(StringComparer.Ordinal);
    private Dictionary<string, long> _cycleBacklog = new(StringComparer.Ordinal);
    private WorkFailure? _lastPassFailure;
    private WorkFailure? _lastItemFailure;

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

    public void Dispose()
    {
        _workerSlots.Dispose();
        _gauges.Dispose();
    }

    /// <summary>Items running in this worker now, for the cap tests.</summary>
    public int InFlight => _settings.Concurrency - _workerSlots.CurrentCount;

    /// <summary>
    /// Runs passes until <paramref name="stopClaiming"/> fires, waking every <c>pollInterval</c> and at once after a pass
    /// that claimed a full batch, then waits for the in-flight shards. A pass claims without waiting for the shards it or an
    /// earlier pass started, so a slow or hung handler holds up only its own shard (FR-018).
    /// <paramref name="cancelHandlers"/> cancels the running handlers, whose attempts are then recorded as
    /// <c>work.handler_cancelled</c>, and <paramref name="abandonRecords"/> abandons the failure records and releases still
    /// running (<c>work</c> contract Shutdown rules 1 to 3).
    /// </summary>
    public async Task RunAsync(CancellationToken stopClaiming, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        while (!stopClaiming.IsCancellationRequested)
        {
            var full = false;
            try
            {
                full = await ClaimPassAsync(started: null, stopClaiming, cancelHandlers, abandonRecords);
                PassSucceeded();
            }
            catch (Exception) when (stopClaiming.IsCancellationRequested)
            {
                // A pass the stop cut short, whatever it failed with, such as coordination.cancelled from a lease acquire,
                // is the stop, not a pass failure, so it writes no 8012.
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

        Task[] running;
        lock (_shardGate)
        {
            running = [.. _running];
        }

        await Task.WhenAll(running);
    }

    /// <summary>
    /// One pass over one page of the directory, which then waits for the shards it started. Returns whether any shard's
    /// claim was a full batch.
    /// </summary>
    public async Task<bool> PassAsync(CancellationToken stopClaiming, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        var started = new List<Task>();
        try
        {
            return await ClaimPassAsync(started, stopClaiming, cancelHandlers, abandonRecords);
        }
        finally
        {
            await Task.WhenAll(started);
        }
    }

    // One pass over one page of the directory: claims each scope's shards and starts their batches, adding each started
    // shard to started when it is not null. Returns whether any shard's claim was a full batch, so the next pass starts
    // at once.
    private async Task<bool> ClaimPassAsync(List<Task>? started, CancellationToken stopClaiming, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        IReadOnlyList<string> page;
        await using (var unit = await _units.OpenAsync(UnitOfWorkScope.Operator, new UnitOfWorkOptions(Module, "work.directory_page"), stopClaiming))
        {
            page = await ScopeDirectory.PageDueAsync(unit, Module, _cursor, _settings.BatchSize, stopClaiming);
            await unit.CommitAsync(stopClaiming);
        }

        // Skew and the backlog are counted over a whole cycle of the directory cursor, so a kind whose skewed items sit on
        // another page is not reported cleared, and the backlog is every due scope's, not one page's (Version skew rule 2,
        // Diagnostics rules 1 and 3).
        var endOfCycle = page.Count < _settings.BatchSize;
        _cursor = endOfCycle ? "" : page[^1];
        var skewed = _cycleSkewed;
        var backlog = _cycleBacklog;

        // The page's scopes claim side by side, sharing the worker's slots, so the worker cap is reachable across scopes and
        // a slow or hung handler in one scope does not hold up another's items (FR-011, FR-018). A scope that finds no free
        // worker slot leaves its shards to other workers or the next pass.
        var scopes = new List<Task<bool>>();
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

            scopes.Add(Task.Run(() => ScopeAsync(listed, scope, skewed, backlog, started, stopClaiming, cancelHandlers, abandonRecords), CancellationToken.None));
        }

        bool[] full;
        try
        {
            full = await Task.WhenAll(scopes);
        }
        catch (OperationCanceledException) when (stopClaiming.IsCancellationRequested)
        {
            // Every scope has finished what it started; a scope cancelled before it claimed throws.
            full = [.. scopes.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result)];
        }
        finally
        {
            // The cycle ends with this page even when one of its scopes failed, so its totals are reported and reset
            // rather than carried into the next cycle. The shard offsets of scopes no longer due go with it.
            if (endOfCycle)
            {
                ReportSkew(skewed);
                _cycleSkewed = new Dictionary<string, long>(StringComparer.Ordinal);
                ReportBacklog(backlog);
                _cycleBacklog = new Dictionary<string, long>(StringComparer.Ordinal);
                lock (_shardGate)
                {
                    _lastOffsets = _offsets;
                    _offsets = new Dictionary<string, int>(StringComparer.Ordinal);
                }
            }
        }

        return full.Any(result => result);
    }

    private async Task<bool> ScopeAsync(
        string listed,
        UnitOfWorkScope scope,
        Dictionary<string, long> skewed,
        Dictionary<string, long> backlog,
        List<Task>? started,
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
                lock (totals)
                {
                    totals[row.Kind] = totals.GetValueOrDefault(row.Kind) + row.Count;
                }

                if (row.Handled)
                {
                    shards[row.Shard] = shards.GetValueOrDefault(row.Shard) + row.Count;
                }
            }
        }

        // A shard's lease is tried only within the in-flight caps (research R6 step 3): one worker slot and one of the
        // scope's slots are reserved first, for the shard's items, and both stay held until its batch ends, across passes.
        // A shard this worker already runs is skipped, and a contended shard hands its reservation to the next shard. The
        // shards are tried from where the scope's last pass stopped, so a backlog in one shard cannot keep the scope's
        // slots from its other shards (Dispatch and apply rules 1 and 3, FR-010).
        var offset = Offset(listed);
        var next = offset;
        var full = false;
        ExceptionDispatchInfo? failed = null;
        try
        {
            foreach (var shard in shards.Keys.Order().Where(shard => shard >= offset).Concat(shards.Keys.Order().Where(shard => shard < offset)))
            {
                if (stopClaiming.IsCancellationRequested)
                {
                    break;
                }

                next = shard;
                if (!TryReserve(listed, shard, out var held))
                {
                    if (held)
                    {
                        continue;
                    }

                    break;
                }

                if (!_workerSlots.Wait(0))
                {
                    Unreserve(listed, shard);
                    break;
                }

                next = (shard + 1) % _settings.Shards;
                ShardLease? lease;
                List<ClaimedItem> claimed;
                try
                {
                    lease = await _leases.TryAcquireAsync(scope, shard, stopClaiming);
                    if (lease is null)
                    {
                        _workerSlots.Release();
                        Unreserve(listed, shard);
                        continue;
                    }
                }
                catch
                {
                    next = shard;
                    _workerSlots.Release();
                    Unreserve(listed, shard);
                    throw;
                }

                try
                {
                    claimed = await ClaimAsync(scope, shard, stopClaiming);
                }
                catch
                {
                    _workerSlots.Release();
                    Unreserve(listed, shard);
                    await lease.ReleaseAsync(abandonRecords);
                    await lease.DisposeAsync();
                    throw;
                }

                full |= claimed.Count == _settings.BatchSize;
                var batch = Start(listed, scope, shard, lease, claimed, stopClaiming, cancelHandlers, abandonRecords);
                if (started is not null)
                {
                    lock (started)
                    {
                        started.Add(batch);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stopClaiming.IsCancellationRequested)
        {
            // The shards already started finish on their own.
        }
        catch (Exception exception)
        {
            // A lease acquire or a claim failed: the shards already started finish on their own, and the failure goes on.
            failed = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            SetOffset(listed, next);
        }

        failed?.Throw();

        // A scope with a shard still running is settled when its last shard ends, so its row keeps the earliest due time
        // of the items that shard has not yet run.
        if (!stopClaiming.IsCancellationRequested && Idle(listed))
        {
            await SettleAsync(scope, stopClaiming);
        }

        return full;
    }

    private async Task<List<ClaimedItem>> ClaimAsync(UnitOfWorkScope scope, int shard, CancellationToken stopClaiming)
    {
        await using var unit = await OpenAsync(scope, "work.claim", stopClaiming);
        var claimed = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, ClaimSql)
            .With(_settings.Shards).With(shard).With(_kinds).With(_lowest).With(_highest).With(_settings.BatchSize)
            .QueryAsync(ClaimedItem.Read, stopClaiming);
        await unit.CommitAsync(stopClaiming);
        return [.. claimed];
    }

    // Starts a claimed shard's batch, tracked until it ends, when it frees the shard's scope slot and, once the scope has
    // no shard running, settles the scope's directory row.
    private Task Start(
        string listed,
        UnitOfWorkScope scope,
        int shard,
        ShardLease lease,
        List<ClaimedItem> claimed,
        CancellationToken stopClaiming,
        CancellationToken cancelHandlers,
        CancellationToken abandonRecords)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = Task.Run(
            async () =>
            {
                await ready.Task;
                try
                {
                    await ShardAsync(scope, lease, claimed, stopClaiming, cancelHandlers, abandonRecords);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Each item's failures are its own; anything else, such as a release that failed, is the pass's, so the
                    // stop's wait for the running batches never fails with it.
                    if (!stopClaiming.IsCancellationRequested)
                    {
                        PassFailed(exception);
                    }
                }
                finally
                {
                    Unreserve(listed, shard);
                }

                if (stopClaiming.IsCancellationRequested || !Idle(listed))
                {
                    return;
                }

                try
                {
                    await SettleAsync(scope, stopClaiming);
                }
                catch (Exception) when (stopClaiming.IsCancellationRequested)
                {
                    // The stop cut the settle short; the next worker settles the scope.
                }
                catch (Exception exception)
                {
                    PassFailed(exception);
                }
            },
            CancellationToken.None);
        lock (_shardGate)
        {
            _running.Add(batch);
        }

        _ = batch.ContinueWith(
            ended =>
            {
                lock (_shardGate)
                {
                    _running.Remove(ended);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        ready.SetResult();
        return batch;
    }

    // Reserves one of the scope's slots for shard, unless this worker already runs it (held) or the scope's slots are used up.
    private bool TryReserve(string listed, int shard, out bool held)
    {
        lock (_shardGate)
        {
            held = _held.Contains((listed, shard));
            if (held || _scopeSlots.GetValueOrDefault(listed) >= _settings.ScopeConcurrency)
            {
                return false;
            }

            _held.Add((listed, shard));
            _scopeSlots[listed] = _scopeSlots.GetValueOrDefault(listed) + 1;
            return true;
        }
    }

    private void Unreserve(string listed, int shard)
    {
        lock (_shardGate)
        {
            _held.Remove((listed, shard));
            if (--_scopeSlots[listed] == 0)
            {
                _scopeSlots.Remove(listed);
            }
        }
    }

    // Whether no shard of the scope is reserved or running in this worker.
    private bool Idle(string listed)
    {
        lock (_shardGate)
        {
            return !_scopeSlots.ContainsKey(listed);
        }
    }

    // The shard the scope's next pass tries first, kept for the scopes of this directory cycle and the last one.
    private int Offset(string listed)
    {
        lock (_shardGate)
        {
            return _offsets.TryGetValue(listed, out var offset) || _lastOffsets.TryGetValue(listed, out offset) ? offset : 0;
        }
    }

    private void SetOffset(string listed, int offset)
    {
        lock (_shardGate)
        {
            _offsets[listed] = offset;
        }
    }

    // Holds the shard's lease, one worker slot, and one of the scope's slots on entry, which the shard's claimed items use
    // one after another. Every unit of a shard's items accepts the shard's fence record, whose row lock serializes them
    // until each unit ends, so an item started beside a shard-mate would only wait on that lock, using up its handler's
    // timeout and holding slots that another shard could use (Dispatch and apply rule 8, FR-014). Each item's attempt
    // record is therefore taken only once the previous item's units have ended.
    private async Task ShardAsync(
        UnitOfWorkScope scope,
        ShardLease lease,
        List<ClaimedItem> claimed,
        CancellationToken stopClaiming,
        CancellationToken cancelHandlers,
        CancellationToken abandonRecords)
    {
        try
        {
            foreach (var item in claimed)
            {
                if (stopClaiming.IsCancellationRequested || !lease.IsSafeToAct)
                {
                    break;
                }

                await RunItemAsync(scope, lease, item, cancelHandlers, abandonRecords);
            }
        }
        finally
        {
            _workerSlots.Release();
            await lease.ReleaseAsync(abandonRecords);
            await lease.DisposeAsync();
        }
    }

    private async Task RunItemAsync(UnitOfWorkScope scope, ShardLease lease, ClaimedItem item, CancellationToken cancelHandlers, CancellationToken abandonRecords)
    {
        try
        {
            await AttemptAsync(scope, lease, item, cancelHandlers, abandonRecords);
            ItemSucceeded();
        }
        catch (Exception) when (cancelHandlers.IsCancellationRequested)
        {
            // The stop cancelled the item's units; its attempt, if recorded, stays counted, and no 8012 is written.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An item's units failed outside the handler, such as the database becoming unreachable. Nothing more is
            // written for it; the attempt record, if it committed, keeps the attempt counted (Retry and poison rule 7).
            ItemFailed(exception);
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
            // A discard here reports the attempt being made, as the apply and the failure record do (Diagnostics event 8010).
            if (await AcceptAsync(unit, lease, item, item.AttemptCount + 1, cancelHandlers) is not null)
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

        // The failure record (Dispatch and apply rule 6). A cancellation at shutdown hands its attempt back, so it never
        // poisons the item, which stays pending and runs again; a crash or a lost lease during the stop still counts
        // (Shutdown rule 2, Retry and poison rule 4; maintainer decision, 2026-11-18, converge).
        var handBack = recorded == WorkClassifier.Shutdown;
        var poison = !handBack && (recorded.Class == WorkFailureClass.Permanent || attempt >= maximum);
        await using (var unit = await OpenAsync(scope, "work.failure", abandonRecords))
        {
            if (await AcceptAsync(unit, lease, item, attempt, abandonRecords) is not null)
            {
                return;
            }

            if (await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, FailureSql)
                .With(item.ItemId).With(attempt).With(recorded.Class.ToCode()).With(recorded.Code.Id).With(poison).With(handBack)
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
        var handlerTimeout = registration.Timeout ?? _settings.HandlerTimeout;
        using var timeout = new CancellationTokenSource(handlerTimeout, _timeProvider);
        using var handlerToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancelHandlers, lease.Lost);
        var activity = WorkTelemetry.StartDispatch(item.Kind, attempt, item.RecordedContext);
        try
        {
            await using var unit = await _units.OpenAsync(
                scope, new UnitOfWorkOptions(Module, "work.apply") { Timeout = handlerTimeout + ApplyMargin }, handlerToken.Token);
            if (await AcceptAsync(unit, lease, item, attempt, handlerToken.Token) is { } discarded)
            {
                WorkTelemetry.End(activity, discarded);
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

            // Which token fired decides, whatever the exception: a handler awaiting a unit statement when its token fires
            // fails with persistence.cancelled, not an OperationCanceledException (Retry and poison rule 1).
            var failure = cancelHandlers.IsCancellationRequested
                ? WorkClassifier.Shutdown
                : timeout.IsCancellationRequested
                    ? WorkClassifier.Timeout
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

    // Accepts the shard lease's token for the shard's fence resource in unit, returning null, or the discard's outcome,
    // lease_lost or fencing_rejected, for event 8010 and the dispatch span alike (Tracing rule 2). A rejection commits
    // nothing. A lost lease writes nothing more (Dispatch and apply rule 3); otherwise the guard decides, even for a lease
    // whose local remaining time has run out, because only the guard knows whether a newer holder has written.
    private async Task<string?> AcceptAsync(IUnitOfWork unit, ShardLease lease, ClaimedItem item, int attempt, CancellationToken cancellationToken)
    {
        if (lease.IsLost)
        {
            WorkLog.ApplyDiscarded(_logger, item.Kind, attempt, WorkOutcomes.LeaseLost);
            return WorkOutcomes.LeaseLost;
        }

        try
        {
            await _fencing.AcceptAsync(unit, lease.Resource, lease.Current, cancellationToken);
            return null;
        }
        catch (GatewayException failure) when (ReferenceEquals(failure.Code, ErrorCodes.CoordinationFencingRejected))
        {
            WorkLog.ApplyDiscarded(_logger, item.Kind, attempt, WorkOutcomes.FencingRejected);
            return WorkOutcomes.FencingRejected;
        }
    }

    private Task SettleAsync(UnitOfWorkScope scope, CancellationToken cancellationToken) => SettleAsync(_units, scope, cancellationToken);

    /// <summary>
    /// Settles <paramref name="scope"/>'s <c>work</c> directory row in its own Read Committed unit: kept with the earliest
    /// pending due time while any item remains, else removed. The dispatcher and the retention job share it.
    /// </summary>
    internal static async Task SettleAsync(IUnitOfWorkFactory units, UnitOfWorkScope scope, CancellationToken cancellationToken)
    {
        await using var unit = await units.OpenAsync(scope, new UnitOfWorkOptions(Module, "work.settle"), cancellationToken);
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

    // The skewed gauge as of the last full cycle of the directory cursor, and event 8008 when a kind's skew appears or clears
    // in the process.
    private void ReportSkew(Dictionary<string, long> skewed)
    {
        foreach (var kind in _skewedKinds.Except(skewed.Keys, StringComparer.Ordinal).ToList())
        {
            _skewedKinds.Remove(kind);
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                _gauges.SetSkewed(parsed, 0);
                WorkLog.ItemsSkewed(_logger, parsed, appeared: false);
            }
        }

        foreach (var (kind, count) in skewed)
        {
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                _gauges.SetSkewed(parsed, count);
                if (_skewedKinds.Add(kind))
                {
                    WorkLog.ItemsSkewed(_logger, parsed, appeared: true);
                }
            }
        }
    }

    // The backlog gauge as of the last full cycle of the directory cursor.
    private void ReportBacklog(Dictionary<string, long> backlog)
    {
        foreach (var kind in _kinds)
        {
            if (WorkKind.TryParse(kind, out var parsed) && parsed is not null)
            {
                _gauges.SetBacklog(parsed, backlog.GetValueOrDefault(kind));
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

    // An item's unit failure writes 8012 once per change of class and code too, with its own state, so a pass that
    // succeeds around a persistent item failure does not write it again; an item whose units all succeed resets it.
    private void ItemFailed(Exception exception)
    {
        var failure = WorkClassifier.Classify(exception);
        lock (_passFailureGate)
        {
            if (_lastItemFailure == failure)
            {
                return;
            }

            _lastItemFailure = failure;
        }

        WorkLog.PassFailed(_logger, failure.Class, failure.Code);
    }

    private void ItemSucceeded()
    {
        lock (_passFailureGate)
        {
            _lastItemFailure = null;
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

using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// One in-process dispatcher over the work fixture's database (ISK-0009 T022, research R18, FR-040): its own data sources
/// and unit factory, handler instances, PostgreSQL lease coordinator, fencing guard, settings overrides, and injection.
/// A test runs several side by side. <see cref="AbandonAsync"/> drops it the way a crashed process would.
/// </summary>
internal sealed class TestDispatcher : IAsyncDisposable
{
    private readonly WorkFixture _work;
    private readonly PostgreSqlDataSources _sources;
    private readonly ConcurrentDictionary<Type, IWorkHandler> _handlers = new();
    private readonly CancellationTokenSource _stopClaiming = new();
    private readonly CancellationTokenSource _cancelHandlers = new();
    private readonly CancellationTokenSource _abandonRecords = new();
    private Task? _running;
    private bool _abandoned;

    /// <param name="proxy">When set, the dispatcher reaches the database only through it, so a test can make it unreachable.</param>
    public TestDispatcher(WorkFixture work, IReadOnlyList<WorkHandlerRegistration> handlers, string workFields = "", OutageProxy? proxy = null)
    {
        _work = work;
        var overrides = proxy is null ? null : new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = "127.0.0.1", ["port"] = proxy.Port };
        _sources = new PostgreSqlDataSources(TestSettings.For(work.Database, overrides), TestCredentialProvider.For(work.Postgres));
        Units = new RecordingUnits(new PostgreSqlUnitOfWorkFactory(_sources, TimeProvider.System, work.Logger));
        var (settings, coordination) = WorkTestSettings.Read(workFields);
        Settings = settings;
        Leases = new PausableLeases(new PostgreSqlLeaseCoordinator(Units, coordination, work.Logger));
        Inner = new WorkDispatcher(
            Units,
            new PostgreSqlFencingGuard(work.Logger),
            new ShardLeases(Leases, settings.LeaseDuration, timeProvider: null),
            settings,
            handlers,
            type => _handlers.GetOrAdd(type, handlerType => (IWorkHandler)Activator.CreateInstance(handlerType)!),
            Injection,
            work.Logger,
            timeProvider: null);
    }

    public WorkDispatcher Inner { get; }

    public WorkSettings Settings { get; }

    /// <summary>The dispatcher's unit factory, which records every unit it opens.</summary>
    public RecordingUnits Units { get; }

    /// <summary>The lease coordinator, whose renewals <see cref="PausableLeases.Pause"/> holds.</summary>
    public PausableLeases Leases { get; }

    public TestInjection Injection { get; } = new();

    /// <summary>This dispatcher's one instance of handler <typeparamref name="T"/>.</summary>
    public T Handler<T>()
        where T : IWorkHandler => (T)_handlers.GetOrAdd(typeof(T), type => (IWorkHandler)Activator.CreateInstance(type)!);

    /// <summary>A bare dispatcher over the fixture's own unit factory, for the start-up tests.</summary>
    public static WorkDispatcher Create(WorkFixture work, IReadOnlyList<WorkHandlerRegistration> handlers, string workFields, IWorkInjection? injection, out WorkSettings settings, out PostgreSqlLeaseCoordinator leases)
    {
        var (read, coordination) = WorkTestSettings.Read(workFields);
        settings = read;
        leases = new PostgreSqlLeaseCoordinator(work.Factory, coordination, work.Logger);
        return new WorkDispatcher(
            work.Factory,
            new PostgreSqlFencingGuard(work.Logger),
            new ShardLeases(leases, settings.LeaseDuration, timeProvider: null),
            settings,
            handlers,
            type => (IWorkHandler)Activator.CreateInstance(type)!,
            injection,
            work.Logger,
            timeProvider: null);
    }

    /// <summary>Runs one pass and returns whether a claim was a full batch.</summary>
    public Task<bool> PassAsync() => Inner.PassAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token);

    /// <summary>Starts one pass in the background, for a test that blocks it at an injection point.</summary>
    public Task<bool> StartPass() => Task.Run(PassAsync);

    /// <summary>Starts the dispatcher's loop.</summary>
    public void Start() => _running = Inner.RunAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token);

    /// <summary>Stops claiming, cancels the running handlers, and waits for the loop.</summary>
    public async Task StopAsync()
    {
        await _stopClaiming.CancelAsync();
        await _cancelHandlers.CancelAsync();
        if (_running is { } running)
        {
            await running;
        }
    }

    /// <summary>
    /// Drops the dispatcher without a stop, as a killed process: its data sources close, so no unit, renewal, or release
    /// of it reaches the database again; every backend of the runtime login holding an open transaction in the class
    /// database is terminated with <c>pg_terminate_backend</c>, which rolls back what it had not committed; and its
    /// blocked hooks fail, so its flow ends without writing. Its leases are never released and expire. Call it while no
    /// other dispatcher of the test holds an open transaction.
    /// </summary>
    public async Task AbandonAsync()
    {
        _abandoned = true;
        await _sources.DisposeAsync();
        await _work.ExecuteAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_work.DatabaseName}' "
            + $"AND usename = '{PostgreSqlFixture.RuntimeLogin}' AND xact_start IS NOT NULL AND pid <> pg_backend_pid()");
        Injection.Abandon();
        Leases.Resume();
    }

    public async ValueTask DisposeAsync()
    {
        Injection.ReleaseAll();
        Leases.Resume();
        if (!_abandoned)
        {
            await StopAsync();
            await _sources.DisposeAsync();
        }

        Inner.Dispose();
        _stopClaiming.Dispose();
        _cancelHandlers.Dispose();
        _abandonRecords.Dispose();
    }
}

/// <summary>The dispatcher's unit factory, recording the scope and operation of every unit opened through it.</summary>
internal sealed class RecordingUnits(IUnitOfWorkFactory inner) : IUnitOfWorkFactory
{
    public ConcurrentQueue<(UnitOfWorkScope Scope, string Operation)> Opened { get; } = new();

    /// <summary>When set, runs with the operation and the caller's token after each unit opens, before it is returned.</summary>
    public Func<string, CancellationToken, Task>? AfterOpen { get; set; }

    public async ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken)
    {
        Opened.Enqueue((scope, options.Operation));
        var unit = await inner.OpenAsync(scope, options, cancellationToken);
        if (AfterOpen is { } hook)
        {
            await hook(options.Operation, cancellationToken);
        }

        return unit;
    }
}

/// <summary>
/// A lease coordinator whose renewals a test can hold, as a dispatcher paused past its lease's expiry: while paused, a
/// renewal waits, and on resume it reaches the real coordinator, which then reports the lease lost.
/// </summary>
internal sealed class PausableLeases(ILeaseCoordinator inner) : ILeaseCoordinator
{
    private volatile TaskCompletionSource _resumed = Resumed();

    public void Pause() => _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Resume() => _resumed.TrySetResult();

    /// <summary>When set, an acquire whose name it returns an exception for throws that exception instead.</summary>
    public Func<CoordinationName, Exception?>? FailAcquire { get; set; }

    /// <summary>
    /// When set, an acquire of a name it returns true for takes the real lease but is granted with nothing remaining, as an
    /// acquire that waited past its lease's expiry is (contract Leases rule 3).
    /// </summary>
    public Func<CoordinationName, bool>? GrantExpired { get; set; }

    public async ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
    {
        if (FailAcquire?.Invoke(name) is { } failure)
        {
            throw failure;
        }

        var acquisition = await inner.AcquireAsync(scope, name, duration, wait, cancellationToken);
        return GrantExpired?.Invoke(name) == true && acquisition.Lease is { } lease
            ? LeaseAcquisition.Acquired(Lease.Grant(lease.Scope, lease.Name, lease.Token, lease.Holder, lease.Issuer, TimeSpan.Zero, TimeProvider.System.GetTimestamp(), lease.SafetyMargin, TimeProvider.System))
            : acquisition;
    }

    public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
    {
        await _resumed.Task.WaitAsync(cancellationToken);
        return await inner.RenewAsync(lease, duration, cancellationToken);
    }

    /// <summary>When set, a release waits for it, or for the caller's token, before it reaches the real coordinator.</summary>
    public Task? HoldRelease { get; set; }

    public async ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        if (HoldRelease is { } held)
        {
            await held.WaitAsync(cancellationToken);
        }

        await inner.ReleaseAsync(lease, cancellationToken);
    }

    private static TaskCompletionSource Resumed()
    {
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resumed.SetResult();
        return resumed;
    }
}

/// <summary>
/// The harness's injection: at each point a test may arm a hook that signals when an item reaches it, and, when it
/// blocks, holds the item there until the test releases it, makes it throw, or abandons the dispatcher.
/// </summary>
internal sealed class TestInjection : IWorkInjection
{
    private readonly ConcurrentDictionary<WorkInjectionPoint, InjectionHook> _hooks = new();

    /// <summary>Arms a hook at <paramref name="point"/>; it blocks unless <paramref name="blocks"/> is false.</summary>
    public InjectionHook At(WorkInjectionPoint point, bool blocks = true) => _hooks[point] = new InjectionHook(blocks);

    public async ValueTask ReachedAsync(WorkInjectionPoint point, Guid itemId, CancellationToken cancellationToken)
    {
        if (_hooks.TryGetValue(point, out var hook))
        {
            await hook.ReachedAsync(itemId, cancellationToken);
        }
    }

    public void Abandon()
    {
        foreach (var hook in _hooks.Values)
        {
            hook.Throw(new GatewayException(ErrorCodes.PersistenceUnavailable));
        }
    }

    public void ReleaseAll()
    {
        foreach (var hook in _hooks.Values)
        {
            hook.Release();
        }
    }
}

/// <summary>One armed injection point.</summary>
internal sealed class InjectionHook(bool blocks)
{
    private readonly TaskCompletionSource<Guid> _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _count;

    /// <summary>Completes with the first item that reached the point.</summary>
    public Task<Guid> Reached => _reached.Task;

    /// <summary>How many times an item reached the point.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Lets every held and later item pass.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>Makes every held and later item throw <paramref name="exception"/>.</summary>
    public void Throw(Exception exception) => _released.TrySetException(exception);

    internal async ValueTask ReachedAsync(Guid itemId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        _reached.TrySetResult(itemId);
        if (blocks)
        {
            await _released.Task.WaitAsync(cancellationToken);
        }
    }
}

/// <summary>Bounded waits on database state, for tests that never wait on a fixed sleep.</summary>
internal static class Eventually
{
    /// <summary>The bound on every wait, after which the test fails.</summary>
    public static TimeSpan Bound { get; } = TimeSpan.FromSeconds(60);

    /// <summary>Re-checks <paramref name="condition"/> until it holds, failing the test after <see cref="Bound"/>.</summary>
    public static async Task HoldsAsync(Func<Task<bool>> condition, string what)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(Bound);
        while (!await condition())
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), bound.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                Assert.Fail($"Timed out waiting for {what}.");
            }
        }
    }

    /// <summary>Waits until item <paramref name="itemId"/> has <paramref name="state"/>.</summary>
    public static Task StateAsync(WorkFixture work, Guid itemId, string state) =>
        HoldsAsync(async () => await work.ScalarAsync<string>($"SELECT coalesce((SELECT state FROM isk_work.items WHERE item_id = '{itemId}'), '')") == state, $"item state {state}");
}

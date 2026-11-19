using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The <c>work-dispatcher</c> composition start-up (<c>work</c> contract Registration rule 2, Shutdown; research R12). Its
/// module name sorts after <c>coordination</c>, so it starts after the coordination start-up has composed the gate and
/// stops before it. Start builds the dispatcher only when <c>work.dispatch</c> was accepted, since hosting refuses a
/// mandatory feature only after every start-up has run; a refused feature starts nothing and writes nothing, and the
/// refusal follows. Otherwise it starts the loop once <see cref="StartAsync"/> has returned, and writes 8001, and starts
/// the recurring job runner beside it over the jobs whose features were accepted. The stop signal is the host's
/// <c>ApplicationStopping</c>, or the stop itself without a host lifetime: it writes 8002, stops claiming and starting
/// jobs at once, and sets the deadline to the signal plus the host's shutdown timeout. A signal during start-up claims
/// nothing. Stop lets in-flight items and job runs finish until the deadline, then cancels their handlers and runs, and
/// abandons the failure records and lease releases still running 2 seconds after it; then it writes 8003, <c>drained</c> or <c>cancelled</c>.
/// Owned by ISK-0009.
/// </summary>
internal sealed class WorkDispatcherStartup : ICompositionStartup, IAsyncDisposable
{
    public const string Module = "work-dispatcher";

    /// <summary>The shutdown timeout without a host's, which is <c>HostOptions</c>' default.</summary>
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(30);

    // How long after the deadline a failure record or lease release may still finish (Shutdown rule 3).
    private static readonly TimeSpan RecordGrace = TimeSpan.FromSeconds(2);

    private readonly Func<WorkDispatcher> _create;
    private readonly Func<JobRunner>? _createJobs;
    private readonly Func<bool> _accepted;
    private readonly Func<IHostApplicationLifetime?> _lifetime;
    private readonly Func<TimeSpan> _shutdownTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopClaiming = new();
    private readonly CancellationTokenSource _cancelHandlers = new();
    private readonly CancellationTokenSource _abandonRecords = new();
    private readonly Lock _gate = new();
    private CancellationTokenRegistration _stopping;
    private TimeSpan _timeout = DefaultShutdownTimeout;
    private DateTimeOffset? _deadline;
    private WorkDispatcher? _dispatcher;
    private Task? _running;
    private Task _jobs = Task.CompletedTask;
    private int _stopped;

    public WorkDispatcherStartup(
        Func<WorkDispatcher> create,
        ILogger? logger,
        Func<bool>? accepted = null,
        Func<JobRunner>? createJobs = null,
        Func<IHostApplicationLifetime?>? lifetime = null,
        Func<TimeSpan>? shutdownTimeout = null,
        TimeProvider? timeProvider = null)
    {
        _create = create;
        _createJobs = createJobs;
        _accepted = accepted ?? (() => true);
        _lifetime = lifetime ?? (() => null);
        _shutdownTimeout = shutdownTimeout ?? (() => DefaultShutdownTimeout);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public string ModuleName => Module;

    /// <summary>The stop deadline, once the stop signal has come.</summary>
    public DateTimeOffset? Deadline
    {
        get
        {
            lock (_gate)
            {
                return _deadline;
            }
        }
    }

    public ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!_accepted())
        {
            return ValueTask.CompletedTask;
        }

        // Read at start, which follows every change the host makes to its options, and precedes the provider's disposal.
        _timeout = _shutdownTimeout();

        // Registered first, so a signal already given, during start-up, stops claiming before the loop's first claim.
        if (_lifetime() is { } lifetime)
        {
            _stopping = lifetime.ApplicationStopping.Register(() => Signal());
        }

        _dispatcher = _create();
        var dispatcher = _dispatcher;
        _running = Task.Run(() => dispatcher.RunAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token), CancellationToken.None);
        if (_createJobs?.Invoke() is { } jobs)
        {
            _jobs = Task.Run(() => jobs.RunAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token), CancellationToken.None);
        }

        WorkLog.DispatcherStarted(_logger);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_running is not { } running || Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        // A job run started before the signal finishes until the deadline too, as an in-flight item does (Shutdown rule 2).
        var deadline = Signal();
        var work = Task.WhenAll(running, _jobs);
        var drained = await FinishesByAsync(work, deadline);
        if (!drained)
        {
            await _cancelHandlers.CancelAsync();
            if (!await FinishesByAsync(work, deadline + RecordGrace))
            {
                await _abandonRecords.CancelAsync();
            }
        }

        await running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await _jobs.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        WorkLog.DispatcherStopped(_logger, drained);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _stopping.DisposeAsync();
        _dispatcher?.Dispose();
        _stopClaiming.Dispose();
        _cancelHandlers.Dispose();
        _abandonRecords.Dispose();
    }

    // The first signal sets the deadline, writes 8002, and stops claiming; a later one returns the same deadline.
    private DateTimeOffset Signal()
    {
        bool first;
        DateTimeOffset deadline;
        lock (_gate)
        {
            first = _deadline is null;
            _deadline ??= _timeProvider.GetUtcNow() + _timeout;
            deadline = _deadline.Value;
        }

        if (first)
        {
            WorkLog.DispatcherStopping(_logger);
            _stopClaiming.Cancel();
        }

        return deadline;
    }

    // Whether the task finishes by the deadline, on the start-up's clock.
    private async Task<bool> FinishesByAsync(Task task, DateTimeOffset deadline)
    {
        var wait = deadline - _timeProvider.GetUtcNow();
        if (task.IsCompleted || wait <= TimeSpan.Zero)
        {
            return task.IsCompleted;
        }

        using var timer = new CancellationTokenSource();
        var first = await Task.WhenAny(task, Task.Delay(wait, _timeProvider, timer.Token));
        await timer.CancelAsync();
        return first == task;
    }
}

using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The <c>work-dispatcher</c> composition start-up (<c>work</c> contract Registration rule 2, Shutdown). Its module name
/// sorts after <c>coordination</c>, so it starts after the coordination start-up has composed the gate and stops before
/// it. Start builds the dispatcher only when <c>work.dispatch</c> was accepted, since hosting refuses a mandatory
/// feature only after every start-up has run; a refused feature starts nothing and writes nothing, and the refusal
/// follows. Otherwise it starts the loop once <see cref="StartAsync"/> has returned, and writes 8001, and starts the
/// recurring job runner beside it over the jobs whose features were accepted. Stop writes 8002, stops claiming and
/// checking jobs, cancels the running handlers, waits for both loops, and writes 8003. Owned by ISK-0009.
/// </summary>
internal sealed class WorkDispatcherStartup : ICompositionStartup, IAsyncDisposable
{
    public const string Module = "work-dispatcher";

    private readonly Func<WorkDispatcher> _create;
    private readonly Func<JobRunner>? _createJobs;
    private readonly Func<bool> _accepted;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopClaiming = new();
    private readonly CancellationTokenSource _cancelHandlers = new();
    private readonly CancellationTokenSource _abandonRecords = new();
    private WorkDispatcher? _dispatcher;
    private Task? _running;
    private Task _jobs = Task.CompletedTask;
    private int _stopped;

    public WorkDispatcherStartup(Func<WorkDispatcher> create, ILogger? logger, Func<bool>? accepted = null, Func<JobRunner>? createJobs = null)
    {
        _create = create;
        _createJobs = createJobs;
        _accepted = accepted ?? (() => true);
        _logger = logger ?? NullLogger.Instance;
    }

    public string ModuleName => Module;

    public ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!_accepted())
        {
            return ValueTask.CompletedTask;
        }

        _dispatcher = _create();
        var dispatcher = _dispatcher;
        _running = Task.Run(() => dispatcher.RunAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token), CancellationToken.None);
        if (_createJobs?.Invoke() is { } jobs)
        {
            _jobs = Task.Run(() => jobs.RunAsync(_stopClaiming.Token), CancellationToken.None);
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

        WorkLog.DispatcherStopping(_logger);
        await _stopClaiming.CancelAsync();
        await _cancelHandlers.CancelAsync();
        var drained = true;
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
            drained = false;
        }

        await _jobs;

        WorkLog.DispatcherStopped(_logger, drained);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _dispatcher?.Dispose();
        _stopClaiming.Dispose();
        _cancelHandlers.Dispose();
        _abandonRecords.Dispose();
    }
}

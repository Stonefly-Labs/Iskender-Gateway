using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// One in-process dispatcher over the work fixture's database (ISK-0009 T022, research R18), with its own PostgreSQL
/// lease coordinator and fencing guard, settings, handlers, and injection.
/// </summary>
internal sealed class TestDispatcher : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopClaiming = new();
    private readonly CancellationTokenSource _cancelHandlers = new();
    private readonly CancellationTokenSource _abandonRecords = new();
    private Task? _running;

    public TestDispatcher(WorkFixture work, IReadOnlyList<WorkHandlerRegistration> handlers, string workFields = "", IWorkInjection? injection = null)
    {
        var (settings, coordination) = WorkTestSettings.Read(workFields);
        Settings = settings;
        Leases = new PostgreSqlLeaseCoordinator(work.Factory, coordination, work.Logger);
        Inner = new WorkDispatcher(
            work.Factory,
            new PostgreSqlFencingGuard(work.Logger),
            new ShardLeases(Leases, settings.LeaseDuration, timeProvider: null),
            settings,
            handlers,
            type => (IWorkHandler)Activator.CreateInstance(type)!,
            injection,
            work.Logger,
            timeProvider: null);
    }

    public WorkDispatcher Inner { get; }

    public WorkSettings Settings { get; }

    public PostgreSqlLeaseCoordinator Leases { get; }

    /// <summary>Runs one pass and returns whether a claim was a full batch.</summary>
    public Task<bool> PassAsync() => Inner.PassAsync(_stopClaiming.Token, _cancelHandlers.Token, _abandonRecords.Token);

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

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Inner.Dispose();
        _stopClaiming.Dispose();
        _cancelHandlers.Dispose();
        _abandonRecords.Dispose();
    }
}

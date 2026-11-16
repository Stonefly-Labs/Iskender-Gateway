using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Container.Lifecycle;

/// <summary>
/// The <c>lifecycle</c> check's phase (container contract, Readiness rule 2): <c>starting</c> until every composition
/// start-up has finished, then <c>started</c>, and <c>stopping</c> from the first stop signal; and the first step of the
/// stop sequence, which waits <c>shutdownDelay</c> while the server keeps serving (Lifecycle rules 1 to 3; research
/// R12). Owned by ISK-0007.
/// </summary>
public sealed class ContainerLifecycle : IHostedLifecycleService
{
    public const string Starting = "starting";

    public const string Started = "started";

    public const string Stopping = "stopping";

    private readonly CompositionStatus? _status;

    private readonly Func<TimeSpan> _shutdownDelay;

    private readonly string _plane;

    private readonly ILogger _logger;

    private readonly TimeProvider _time;

    private int _stopping;

    /// <summary>
    /// With no <paramref name="status"/>, which only a container without a composition root has, the phase stays
    /// <c>starting</c>, and the host's start fails with <c>hosting.registration_missing</c> instead of a container error.
    /// <paramref name="shutdownDelay"/> is read only when the stop sequence starts, so building the lifecycle validates
    /// nothing.
    /// </summary>
    public ContainerLifecycle(CompositionStatus? status, Func<TimeSpan> shutdownDelay, string plane, ILoggerFactory? loggerFactory = null, TimeProvider? time = null)
    {
        _status = status;
        _shutdownDelay = shutdownDelay;
        _plane = plane;
        _logger = loggerFactory?.CreateLogger(ContainerLog.Category) ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary><c>starting</c>, <c>started</c>, or <c>stopping</c>.</summary>
    public string Phase =>
        Volatile.Read(ref _stopping) == 1 ? Stopping
        : _status?.Phase == CompositionPhase.Started ? Started
        : Starting;

    /// <summary>True only while the phase is <c>started</c>.</summary>
    public bool IsReady => Phase == Started;

    /// <summary>
    /// Turns the phase <c>stopping</c> and writes event 7004, at once, on the first stop signal, during start-up too. Later
    /// calls change nothing, so a second signal neither writes again nor shortens the sequence.
    /// </summary>
    public void BeginStopping()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            ContainerLog.LogStopRequested(_logger, _plane);
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The host's first stop step: <c>stopping</c>, then <c>shutdownDelay</c> while the server keeps serving, so load
    /// balancers can drain. A cancelled <paramref name="cancellationToken"/> ends the wait, not the sequence.
    /// </summary>
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        BeginStopping();
        var delay = _shutdownDelay();
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

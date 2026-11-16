using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.Hosting;

namespace Iskender.Infrastructure.Container.Lifecycle;

/// <summary>
/// The <c>lifecycle</c> check's phase (container contract, Readiness rule 2): <c>starting</c> until every composition
/// start-up has finished, then <c>started</c>, and <c>stopping</c> from the first stop signal. Owned by ISK-0007.
/// </summary>
public sealed class ContainerLifecycle : IHostedLifecycleService
{
    public const string Starting = "starting";

    public const string Started = "started";

    public const string Stopping = "stopping";

    private readonly CompositionStatus? _status;

    private int _stopping;

    /// <summary>
    /// With no <paramref name="status"/>, which only a container without a composition root has, the phase stays
    /// <c>starting</c>, and the host's start fails with <c>hosting.registration_missing</c> instead of a container error.
    /// </summary>
    public ContainerLifecycle(CompositionStatus? status)
    {
        _status = status;
    }

    /// <summary><c>starting</c>, <c>started</c>, or <c>stopping</c>.</summary>
    public string Phase =>
        Volatile.Read(ref _stopping) == 1 ? Stopping
        : _status?.Phase == CompositionPhase.Started ? Started
        : Starting;

    /// <summary>True only while the phase is <c>started</c>.</summary>
    public bool IsReady => Phase == Started;

    /// <summary>Turns the phase <c>stopping</c>. Later calls change nothing.</summary>
    public void BeginStopping() => Volatile.Write(ref _stopping, 1);

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        BeginStopping();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

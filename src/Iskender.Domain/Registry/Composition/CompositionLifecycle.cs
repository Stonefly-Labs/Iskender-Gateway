using System.Globalization;
using System.Runtime.ExceptionServices;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// The composition's part in the host's start and stop (hosting contract, Start-up rules 1 to 8). In
/// <see cref="StartingAsync"/>, before any hosted service starts, it checks every module's requirements and then
/// validates the configuration. In <see cref="StartAsync"/> it runs every <see cref="ICompositionStartup"/> in ordinal
/// module-name order, and in <see cref="StoppedAsync"/> it stops them in reverse order. Owned by ISK-0006, as Hosting's
/// start-up; moved here by ISK-0007.
/// </summary>
public sealed class CompositionLifecycle : IHostedLifecycleService
{
    private readonly IReadOnlyList<CompositionModule> _modules;

    private readonly CompositionConfiguration _configuration;

    private readonly CompositionStatus _status;

    private readonly Func<IEnumerable<ICompositionStartup>> _startups;

    private readonly ILogger _logger;

    private readonly List<ICompositionStartup> _started = [];

    private GatewayException? _refusal;

    private int _composed;

    /// <summary><paramref name="startups"/> is resolved only in <see cref="StartAsync"/>, so building the lifecycle never validates early.</summary>
    public CompositionLifecycle(
        IEnumerable<CompositionModule> modules,
        CompositionConfiguration configuration,
        CompositionStatus status,
        Func<IEnumerable<ICompositionStartup>> startups,
        ILoggerFactory? loggerFactory)
    {
        _modules = [.. modules];
        _configuration = configuration;
        _status = status;
        _startups = startups;
        _logger = loggerFactory?.CreateLogger(CompositionLog.Category) ?? NullLogger.Instance;
    }

    /// <summary>
    /// Runs the requirement check and validation once, with events 6001 to 6003, so an entry point can call it before the
    /// host starts. A later call, <see cref="StartingAsync"/> included, finds the stored outcome and writes nothing.
    /// </summary>
    public Task EnsureComposableAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _refusal) is { } refusal)
        {
            throw refusal;
        }

        if (Volatile.Read(ref _composed) == 1)
        {
            return Task.CompletedTask;
        }

        if (CompositionModules.FirstUnmet(_modules, rootRegistered: true) is var (missing, requiredBy))
        {
            var failure = CompositionLog.RegistrationMissing(_logger, missing, requiredBy);
            Volatile.Write(ref _refusal, failure);
            throw failure;
        }

        try
        {
            var snapshot = _configuration.Validate(cancellationToken);
            if (Interlocked.Exchange(ref _composed, 1) == 0)
            {
                CompositionLog.LogComposed(_logger, _modules.Select(module => module.Name), _modules.SelectMany(module => module.Sections), snapshot.EnvironmentName, snapshot.Revision);
            }
        }
        catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
        {
            CompositionLog.LogStartRefused(_logger, ErrorCount(rejected));
            Volatile.Write(ref _refusal, rejected);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => EnsureComposableAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = _configuration.Validate(cancellationToken);
        foreach (var startup in _startups().OrderBy(startup => startup.ModuleName, StringComparer.Ordinal))
        {
            _started.Add(startup);
            await startup.StartAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        _status.Phase = CompositionPhase.Started;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Stops every started start-up in reverse order, after the server and every other hosted service have stopped, so
    /// requests in flight finish before a lease is released (hosting contract, Start-up rule 8; research R5). Each stop
    /// runs even when <paramref name="cancellationToken"/> is cancelled or an earlier stop fails, and the first failure
    /// is rethrown once all have run.
    /// </summary>
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopStartedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _status.Phase = CompositionPhase.Stopped;
        }
    }

    private async Task StopStartedAsync(CancellationToken cancellationToken)
    {
        ExceptionDispatchInfo? first = null;
        for (var index = _started.Count - 1; index >= 0; index--)
        {
            try
            {
                await _started[index].StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                first ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        _started.Clear();
        first?.Throw();
    }

    private static int ErrorCount(GatewayException rejected) =>
        rejected.Error?.Diagnostics?.Values.FirstOrDefault(pair => pair.Key == "errorCount").Value is { } count
            ? int.Parse(count, CultureInfo.InvariantCulture)
            : 1;
}

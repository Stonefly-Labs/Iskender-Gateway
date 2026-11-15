using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Hosting.Configuration;
using Iskender.Hosting.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Hosting.Composition;

/// <summary>
/// Iskender's part in the host's start and stop (hosting contract, Start-up rules 2, 5, 6, and 8). In
/// <see cref="StartingAsync"/>, before any hosted service starts, it checks every module's requirements and then
/// validates the configuration. In <see cref="StartAsync"/> it runs every <see cref="ICompositionStartup"/> in ordinal
/// module-name order, and in <see cref="StopAsync"/> it stops them in reverse order. Owned by ISK-0006.
/// </summary>
internal sealed class IskenderStartup : IHostedLifecycleService
{
    private readonly IReadOnlyList<CompositionModule> _modules;

    private readonly IskenderConfiguration _configuration;

    private readonly Func<IEnumerable<ICompositionStartup>> _startups;

    private readonly ILogger _logger;

    private readonly List<ICompositionStartup> _started = [];

    private int _composed;

    /// <summary><paramref name="startups"/> is resolved only in <see cref="StartAsync"/>, so building a startup never validates early.</summary>
    public IskenderStartup(IEnumerable<CompositionModule> modules, IskenderConfiguration configuration, Func<IEnumerable<ICompositionStartup>> startups, ILoggerFactory? loggerFactory)
    {
        _modules = [.. modules];
        _configuration = configuration;
        _startups = startups;
        _logger = loggerFactory?.CreateLogger(HostingLog.Category) ?? NullLogger.Instance;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (CompositionGuard.FirstUnmet(_modules) is var (missing, requiredBy))
        {
            HostingLog.LogRegistrationMissing(_logger, missing, requiredBy);
            throw CompositionGuard.Missing(missing, requiredBy);
        }

        try
        {
            var snapshot = _configuration.Validate(cancellationToken);
            if (Interlocked.Exchange(ref _composed, 1) == 0)
            {
                HostingLog.LogComposed(_logger, _modules.Select(module => module.Name), _modules.SelectMany(module => module.Sections), snapshot.EnvironmentName, snapshot.Revision);
            }
        }
        catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
        {
            HostingLog.LogStartRefused(_logger, ErrorCount(rejected));
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var snapshot = _configuration.Validate(cancellationToken);
        foreach (var startup in _startups().OrderBy(startup => startup.ModuleName, StringComparer.Ordinal))
        {
            _started.Add(startup);
            await startup.StartAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        for (var index = _started.Count - 1; index >= 0; index--)
        {
            await _started[index].StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _started.Clear();
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static int ErrorCount(GatewayException rejected) =>
        rejected.Error?.Diagnostics?.Values.FirstOrDefault(pair => pair.Key == "errorCount").Value is { } count
            ? int.Parse(count, CultureInfo.InvariantCulture)
            : 1;
}

using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Container.Diagnostics;
using Iskender.Infrastructure.Container.Lifecycle;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Container.Health;

/// <summary>
/// Computes one readiness report per request from the five checks, in the contract's order (container contract,
/// Readiness rules 1 to 9; research R11). Probes are resolved when first needed, each behind a <see cref="ProbeGate{T}"/>,
/// and the persistence and coordination probes run concurrently. Each change in a check's reason writes event 7003 once.
/// Owned by ISK-0007.
/// </summary>
public sealed class ReadinessEvaluator
{
    /// <summary>The time a probe call may take beyond its own timeout (container contract, Readiness rule 8).</summary>
    public static readonly TimeSpan ProbeAllowance = TimeSpan.FromSeconds(2);

    private const string NotComposed = "not_composed";

    private const string PersistenceNamespace = "persistence";

    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly ContainerLifecycle _lifecycle;
    private readonly ContainerSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _last = new(StringComparer.Ordinal);
    private Gates? _gates;

    public ReadinessEvaluator(IServiceProvider services, ContainerLifecycle lifecycle, ContainerSettings settings, TimeProvider? time, ILoggerFactory? loggerFactory)
    {
        _services = services;
        _lifecycle = lifecycle;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        _logger = loggerFactory?.CreateLogger(ContainerLog.Category) ?? NullLogger.Instance;
    }

    /// <summary>Evaluates every check once. Each caller waits on shared probe calls with its own token.</summary>
    public async ValueTask<ReadinessReport> EvaluateAsync(CancellationToken cancellationToken)
    {
        var gates = Probes();
        var lifecycle = _lifecycle.Phase;
        var persistence = Persistence(gates.Persistence, cancellationToken);
        var coordination = Coordination(gates.Coordination, cancellationToken);
        await Task.WhenAll(persistence, coordination).ConfigureAwait(false);
        var (capability, refused) = Capability();

        ReadinessCheck[] checks =
        [
            new("lifecycle", lifecycle == ContainerLifecycle.Started, lifecycle),
            await persistence.ConfigureAwait(false),
            await coordination.ConfigureAwait(false),
            SingleInstance(),
            capability,
        ];
        Record(checks);
        return new ReadinessReport(ReadinessReport.CurrentVersion, checks.All(check => check.Ready), checks, refused);
    }

    private static async Task<ReadinessCheck> Persistence(ProbeGate<PersistenceHealthResult>? gate, CancellationToken cancellationToken)
    {
        if (gate is null)
        {
            return new("persistence", true, NotComposed);
        }

        var reason = await Reason(gate, result => result.Reason, PersistenceHealthReason.Unexpected, cancellationToken).ConfigureAwait(false);
        return new("persistence", reason is PersistenceHealthReason.Ok or PersistenceHealthReason.MigrationInProgress, reason.ToCode());
    }

    private static async Task<ReadinessCheck> Coordination(ProbeGate<CoordinationHealthResult>? gate, CancellationToken cancellationToken)
    {
        if (gate is null)
        {
            return new("coordination", true, NotComposed);
        }

        var result = await Reason<CoordinationHealthResult, CoordinationHealthResult?>(gate, result => result, null, cancellationToken).ConfigureAwait(false)
            ?? CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unexpected);
        return new("coordination", result.Status == CoordinationHealthStatus.Healthy, result.Reason.ToCode());
    }

    // A probe that throws or outlasts its bound reports its port's unexpected reason; the caller hanging up still throws.
    private static async Task<TReason> Reason<T, TReason>(ProbeGate<T> gate, Func<T, TReason> reason, TReason unexpected, CancellationToken cancellationToken)
    {
        try
        {
            return reason(await gate.GetAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return unexpected;
        }
    }

    // The listeners bind while the start-ups run, so the guard may not be composed yet: until then the lease is not
    // verified, which the lifecycle check's starting already keeps from reading ready (maintainer decision, 2026-11-16).
    private ReadinessCheck SingleInstance()
    {
        ISingleInstanceStatus? status;
        try
        {
            status = _services.GetService<ISingleInstanceStatus>();
        }
        catch (GatewayException notComposed) when (notComposed.Code == ErrorCodes.CoordinationCapabilityUnsupported)
        {
            return new("single_instance", true, SingleInstanceState.Unverified.ToCode());
        }

        if (status is null)
        {
            return new("single_instance", true, NotComposed);
        }

        return status.State switch
        {
            SingleInstanceState.Lost => new("single_instance", false, "single_instance_lost"),
            SingleInstanceState.HeldByOther => new("single_instance", false, "single_instance_held_by_other"),
            var state => new("single_instance", true, state.ToCode()),
        };
    }

    // The report exists once the composition start-ups have run; before that, nothing has been refused yet.
    private (ReadinessCheck Check, IReadOnlyList<RefusedFeature> Refused) Capability()
    {
        CapabilityReport? report;
        try
        {
            report = _services.GetService<CapabilityReport>();
        }
        catch (GatewayException)
        {
            report = null;
        }

        RefusedFeature[] refused = report is null
            ? []
            : [.. report.Features
                .Where(feature => !feature.Accepted && !feature.Feature.MandatoryPath)
                .Select(feature => new RefusedFeature(feature.Feature.Name, feature.Feature.Module, feature.Reason.ToCode()))];
        return (new("capability", true, refused.Length == 0 ? "none_refused" : "refused_features"), refused);
    }

    private void Record(IEnumerable<ReadinessCheck> checks)
    {
        lock (_gate)
        {
            foreach (var check in checks)
            {
                var from = _last.GetValueOrDefault(check.Name);
                if (from != check.Reason)
                {
                    _last[check.Name] = check.Reason;
                    ContainerLog.LogReadinessChanged(_logger, check.Name, from, check.Reason);
                }
            }
        }
    }

    private Gates Probes()
    {
        lock (_gate)
        {
            return _gates ??= new Gates(
                _services.GetService<IPersistenceHealthProbe>() is { } persistence
                    ? new(persistence.CheckAsync, _settings.ReadinessCacheDuration, Bound(PersistenceProbeTimeout()), _time)
                    : null,
                _services.GetService<ICoordinationHealthProbe>() is { } coordination
                    ? new(coordination.CheckAsync, _settings.ReadinessCacheDuration, Bound(_services.GetService<CoordinationSettings>()?.HealthProbeTimeout), _time)
                    : null);
        }
    }

    // The container library references no adapter, so it reads the persistence section's probe timeout from the
    // validated snapshot by its registered schema, as ISK-0004's contract names it.
    private TimeSpan? PersistenceProbeTimeout()
    {
        var schema = _services.GetService<ConfigurationSchemaRegistry>()?.Schemas.FirstOrDefault(registered => registered.Namespace == PersistenceNamespace);
        return schema is null ? null : _services.GetRequiredService<ConfigurationSnapshot>().GetSection(schema).GetDuration("healthProbeTimeout");
    }

    private static TimeSpan Bound(TimeSpan? timeout) => (timeout ?? DefaultProbeTimeout) + ProbeAllowance;

    private sealed record Gates(ProbeGate<PersistenceHealthResult>? Persistence, ProbeGate<CoordinationHealthResult>? Coordination);
}

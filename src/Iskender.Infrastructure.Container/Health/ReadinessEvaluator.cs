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
/// Readiness rules 1 to 9 and 11; research R11). Probes are resolved when first needed, each behind a <see cref="ProbeGate{T}"/>,
/// and the persistence and coordination probes run concurrently. Each change in a check's reason writes event 7003 once.
/// A check whose read throws reports <c>unexpected</c> and writes event 7007 with the exception's type name only, so every
/// answer is a report. Owned by ISK-0007.
/// </summary>
public sealed class ReadinessEvaluator
{
    /// <summary>The time a probe call may take beyond its own timeout (container contract, Readiness rule 8).</summary>
    public static readonly TimeSpan ProbeAllowance = TimeSpan.FromSeconds(2);

    private const string NotComposed = "not_composed";

    private const string Unexpected = "unexpected";

    private const string PersistenceNamespace = "persistence";

    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly ContainerLifecycle _lifecycle;
    private readonly ContainerSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _last = new(StringComparer.Ordinal);
    private Slot<PersistenceHealthResult>? _persistence;
    private Slot<CoordinationHealthResult>? _coordination;

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
        var persistence = Persistence(cancellationToken);
        var coordination = Coordination(cancellationToken);
        await Task.WhenAll(persistence, coordination).ConfigureAwait(false);

        // Read once the probes have answered, so a request in flight when the stop signal arrives answers stopping
        // (Lifecycle rule 1).
        var lifecycle = _lifecycle.Phase;
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

    private async Task<ReadinessCheck> Persistence(CancellationToken cancellationToken)
    {
        ProbeGate<PersistenceHealthResult>? gate;
        try
        {
            gate = PersistenceGate();
        }
        catch (Exception failure)
        {
            return Failed("persistence", failure);
        }

        if (gate is null)
        {
            return new("persistence", true, NotComposed);
        }

        var reason = await Reason(gate, "persistence", result => result.Reason, PersistenceHealthReason.Unexpected, cancellationToken).ConfigureAwait(false);
        return new("persistence", reason is PersistenceHealthReason.Ok or PersistenceHealthReason.MigrationInProgress, reason.ToCode());
    }

    private async Task<ReadinessCheck> Coordination(CancellationToken cancellationToken)
    {
        ProbeGate<CoordinationHealthResult>? gate;
        try
        {
            gate = CoordinationGate();
        }
        catch (Exception failure)
        {
            return Failed("coordination", failure);
        }

        if (gate is null)
        {
            return new("coordination", true, NotComposed);
        }

        var result = await Reason<CoordinationHealthResult, CoordinationHealthResult?>(gate, "coordination", result => result, null, cancellationToken).ConfigureAwait(false)
            ?? CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unexpected);
        return new("coordination", result.Status == CoordinationHealthStatus.Healthy, result.Reason.ToCode());
    }

    // A probe that throws or outlasts its bound reports its port's unexpected reason; the caller hanging up still throws.
    private async Task<TReason> Reason<T, TReason>(ProbeGate<T> gate, string check, Func<T, TReason> reason, TReason unexpected, CancellationToken cancellationToken)
    {
        try
        {
            return reason(await gate.GetAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            ContainerLog.LogReadinessCheckFailed(_logger, check, failure.GetType().Name);
            return unexpected;
        }
    }

    // Only the type name is written, never the message (container contract, Diagnostics rules 6 and 8).
    private ReadinessCheck Failed(string check, Exception failure)
    {
        ContainerLog.LogReadinessCheckFailed(_logger, check, failure.GetType().Name);
        return new(check, false, Unexpected);
    }

    // The listeners bind while the start-ups run, so the guard may not be composed yet: until then the lease is not
    // verified, which the lifecycle check's starting already keeps from reading ready (maintainer decision, 2026-11-16).
    private ReadinessCheck SingleInstance()
    {
        SingleInstanceState state;
        try
        {
            if (_services.GetService<ISingleInstanceStatus>() is not { } status)
            {
                return new("single_instance", true, NotComposed);
            }

            state = status.State;
        }
        catch (GatewayException notComposed) when (notComposed.Code == ErrorCodes.CoordinationCapabilityUnsupported)
        {
            return new("single_instance", true, SingleInstanceState.Unverified.ToCode());
        }
        catch (Exception failure)
        {
            return Failed("single_instance", failure);
        }

        return state switch
        {
            SingleInstanceState.Lost => new("single_instance", false, "single_instance_lost"),
            SingleInstanceState.HeldByOther => new("single_instance", false, "single_instance_held_by_other"),
            _ => new("single_instance", true, state.ToCode()),
        };
    }

    // The report exists once the composition start-ups have run; before that, nothing has been refused yet.
    private (ReadinessCheck Check, IReadOnlyList<RefusedFeature> Refused) Capability()
    {
        try
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
        catch (Exception failure)
        {
            return (Failed("capability", failure), []);
        }
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

    // Each gate is built once, when first needed. A build that throws is not kept, so the next evaluation tries again.
    private ProbeGate<PersistenceHealthResult>? PersistenceGate()
    {
        lock (_gate)
        {
            return (_persistence ??= new(
                _services.GetService<IPersistenceHealthProbe>() is { } persistence
                    ? new(persistence.CheckAsync, _settings.ReadinessCacheDuration, Bound(PersistenceProbeTimeout()), _time)
                    : null)).Gate;
        }
    }

    private ProbeGate<CoordinationHealthResult>? CoordinationGate()
    {
        lock (_gate)
        {
            return (_coordination ??= new(
                _services.GetService<ICoordinationHealthProbe>() is { } coordination
                    ? new(coordination.CheckAsync, _settings.ReadinessCacheDuration, Bound(_services.GetService<CoordinationSettings>()?.HealthProbeTimeout), _time)
                    : null)).Gate;
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

    private sealed record Slot<T>(ProbeGate<T>? Gate);
}

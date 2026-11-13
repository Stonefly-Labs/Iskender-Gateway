using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The coordination health probe (FR-025, contract Health, research R17), composed from the probes of the distributed
/// coordinators in use. It checks Redis, then PostgreSQL, and reports the first unhealthy result. With no primitive on a
/// distributed coordinator it is healthy with <c>not_configured</c>; a primitive on one without its settings is unhealthy
/// with <c>not_configured</c>. The probes share one <c>healthProbeTimeout</c> bound, so the whole check returns within it
/// however many coordinators are in use; a probe still running when it ends is a timeout. A coordinator condition is a reason,
/// never an exception, and only the caller's token throws, with <c>coordination.cancelled</c>. Event 5009 is written when
/// the result differs from the last one. Nothing in ISK-0005 calls it; ISK-0007 maps it onto readiness. Owned by ISK-0005.
/// </summary>
public sealed class CoordinationHealthProbe : ICoordinationHealthProbe
{
    private const string Module = "coordination";
    private const string Operation = "health.check";

    private readonly CoordinationSettings _settings;
    private readonly bool _postgreSqlConfigured;
    private readonly ICoordinationHealthProbe? _redis;
    private readonly ICoordinationHealthProbe? _postgreSql;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private CoordinationHealthResult? _last;

    /// <summary>
    /// <paramref name="postgreSqlConfigured"/> says whether the <c>persistence</c> section, which the caller reads, is
    /// configured. A probe is <c>null</c> when its coordinator was not built.
    /// </summary>
    public CoordinationHealthProbe(
        CoordinationSettings settings,
        bool postgreSqlConfigured,
        ICoordinationHealthProbe? redis,
        ICoordinationHealthProbe? postgreSql,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _postgreSqlConfigured = postgreSqlConfigured;
        _redis = redis;
        _postgreSql = postgreSql;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        RequireNotCancelled(cancellationToken);
        var result = await CheckAllAsync(cancellationToken);
        bool changed;
        lock (_gate)
        {
            changed = _last != result;
            _last = result;
        }

        if (changed)
        {
            CoordinationLog.LogHealthChanged(_logger, result);
        }

        return result;
    }

    /// <summary>Throws <c>coordination.cancelled</c> when the caller's <paramref name="cancellationToken"/> has fired.</summary>
    public static void RequireNotCancelled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled);
        }
    }

    /// <summary>
    /// The unhealthy result for a probe failure of <paramref name="failureClass"/>: a connection failure is
    /// <c>unreachable</c>, a refused or missing credential <c>authentication_failed</c>, a timeout <c>timeout</c>, and
    /// anything else <c>unexpected</c>, which writes event 5010.
    /// </summary>
    public static CoordinationHealthResult ResultFor(CoordinationFailureClass failureClass, CoordinatorKind coordinator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        switch (failureClass)
        {
            case CoordinationFailureClass.Unavailable:
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable);
            case CoordinationFailureClass.AuthenticationFailed:
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed);
            case CoordinationFailureClass.Timeout:
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout);
            default:
                CoordinationLog.LogUnexpectedFailure(logger, Module, Operation, coordinator, CoordinationFailureClass.Unexpected);
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unexpected);
        }
    }

    private async ValueTask<CoordinationHealthResult> CheckAllAsync(CancellationToken cancellationToken)
    {
        var usesRedis = Uses(CoordinatorKind.Redis);
        var usesPostgreSql = Uses(CoordinatorKind.PostgreSql);
        if (!usesRedis && !usesPostgreSql)
        {
            return CoordinationHealthResult.NothingDistributed;
        }

        if ((usesRedis && (!_settings.HasRedis || _redis is null)) || (usesPostgreSql && (!_postgreSqlConfigured || _postgreSql is null)))
        {
            return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.NotConfigured);
        }

        using var bound = new CancellationTokenSource(_settings.HealthProbeTimeout, _timeProvider);
        if (usesRedis && await CheckOneAsync(_redis!, CoordinatorKind.Redis, bound, cancellationToken) is { Status: CoordinationHealthStatus.Unhealthy } redis)
        {
            return redis;
        }

        if (usesPostgreSql && await CheckOneAsync(_postgreSql!, CoordinatorKind.PostgreSql, bound, cancellationToken) is { Status: CoordinationHealthStatus.Unhealthy } postgreSql)
        {
            return postgreSql;
        }

        return CoordinationHealthResult.Healthy;
    }

    // A probe that overruns the shared bound is a timeout, and one that fails in a way it did not classify is unexpected.
    private async ValueTask<CoordinationHealthResult> CheckOneAsync(
        ICoordinationHealthProbe probe,
        CoordinatorKind coordinator,
        CancellationTokenSource bound,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        try
        {
            return await probe.CheckAsync(linked.Token).AsTask().WaitAsync(linked.Token);
        }
        catch (Exception caught) when (!cancellationToken.IsCancellationRequested)
        {
            return bound.IsCancellationRequested
                ? CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout)
                : ResultFor(caught is GatewayException failure && CoordinationFailures.ClassOf(failure) is { } failureClass ? failureClass : CoordinationFailureClass.Unexpected, coordinator, _logger);
        }
        catch (Exception)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled);
        }
    }

    private bool Uses(CoordinatorKind coordinator) =>
        _settings.AtomicCoordinator == coordinator || _settings.LeaseCoordinator == coordinator || _settings.CacheCoordinator == coordinator;
}

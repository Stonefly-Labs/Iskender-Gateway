using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// PostgreSQL's part of the coordination health probe (FR-025, research R17): one read of <c>cache_generations</c> in an
/// operator unit, bounded by <c>healthProbeTimeout</c>. A coordinator condition is a reason, never an exception; only the
/// caller's token throws, with <c>coordination.cancelled</c>. The unit reports its own failure, so a failed connection or
/// a refused credential writes event 5003 and an unexpected failure writes event 5010 once, from the unit. Owned by ISK-0005.
/// </summary>
public sealed class PostgreSqlCoordinationHealth : ICoordinationHealthProbe
{
    private const string Module = "coordination";

    private const string ProbeSql = "SELECT 1 FROM isk_coordination.cache_generations WHERE {scope} LIMIT 1";

    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCoordinationHealth(IUnitOfWorkFactory units, CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        CoordinationHealthProbe.RequireNotCancelled(cancellationToken);
        using var bound = new CancellationTokenSource(_settings.HealthProbeTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        try
        {
            await CoordinationUnits.RunAsync(
                _units,
                CoordinationScope.Operator,
                "health.check",
                _settings.HealthProbeTimeout,
                async (unit, token) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.CacheGenerations, ProbeSql).QueryAsync(reader => reader.GetInt32(0), token),
                linked.Token,
                new CoordinationReport(_logger, Module, CoordinationPrimitive.CacheInvalidation),
                writes: false).AsTask().WaitAsync(linked.Token);
            return CoordinationHealthResult.Healthy;
        }
        catch (Exception caught)
        {
            CoordinationHealthProbe.RequireNotCancelled(cancellationToken);
            if (bound.IsCancellationRequested)
            {
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout);
            }

            // A classified unexpected failure was mapped, and its event 5010 written, by the unit.
            return caught is GatewayException failure && CoordinationFailures.ClassOf(failure) is { } classified
                ? classified == CoordinationFailureClass.Unexpected
                    ? CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unexpected)
                    : CoordinationHealthProbe.ResultFor(classified, CoordinatorKind.PostgreSql, _logger)
                : CoordinationHealthProbe.ResultFor(CoordinationFailureClass.Unexpected, CoordinatorKind.PostgreSql, _logger);
        }
    }
}

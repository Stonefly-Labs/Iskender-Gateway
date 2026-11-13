using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// Deletes expired coordination rows of one scope (FR-013a, research R8 and R22). Expired rows already count as absent,
/// so purging only reclaims space. It deletes expired <c>coordination_values</c> and <c>leases</c> rows, under row-level
/// security, and never a <c>fences</c> or <c>cache_generations</c> row, whose loss would let a stale token or a stale
/// cache entry through. It schedules nothing: ISK-0009 runs it across scopes. Its operation is recorded as module
/// <c>coordination</c>, primitive <c>atomic_operations</c>, the first rows it purges, and a failure writes events 5003,
/// 5004, and 5010 as every other PostgreSQL coordinator path does. Owned by ISK-0005.
/// </summary>
public sealed class CoordinationMaintenance
{
    private const string PurgeValuesSql =
        "DELETE FROM isk_coordination.coordination_values WHERE {scope} AND expires_at <= clock_timestamp() AND key_hash IN "
        + "(SELECT key_hash FROM isk_coordination.coordination_values WHERE {scope} AND expires_at <= clock_timestamp() LIMIT $2)";

    private const string PurgeLeasesSql =
        "DELETE FROM isk_coordination.leases WHERE {scope} AND expires_at <= clock_timestamp() AND key_hash IN "
        + "(SELECT key_hash FROM isk_coordination.leases WHERE {scope} AND expires_at <= clock_timestamp() LIMIT $2)";

    private const string Module = "coordination";

    private const string Operation = "maintenance.purge_expired";

    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public CoordinationMaintenance(IUnitOfWorkFactory units, CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Deletes at most <paramref name="batch"/> expired rows of <paramref name="scope"/> in one unit, atomic operations
    /// first and then leases, and returns how many it deleted. The caller repeats it until it returns fewer than
    /// <paramref name="batch"/>. A <paramref name="batch"/> of zero or less fails with <c>core.value.invalid</c>.
    /// </summary>
    public ValueTask<int> PurgeExpiredAsync(CoordinationScope scope, int batch, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationScopeRequired, CoordinationFailureClass.ScopeViolation);
        }

        if (batch <= 0)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return CoordinationTelemetry.MeasureAsync(Module, CoordinationPrimitive.AtomicOperations, Operation, CoordinatorKind.PostgreSql, _timeProvider, () =>
            CoordinationUnits.RunAsync(
                _units,
                scope,
                Operation,
                _settings.OperationTimeout,
                async (unit, token) =>
                {
                    var adapter = PostgreSqlUnitOfWork.From(unit);
                    var deleted = await adapter.Command(CoordinationTables.Values, PurgeValuesSql).With((long)batch).ExecuteAsync(token);
                    if (deleted < batch)
                    {
                        deleted += await adapter.Command(CoordinationTables.Leases, PurgeLeasesSql).With((long)(batch - deleted)).ExecuteAsync(token);
                    }

                    return deleted;
                },
                cancellationToken,
                new CoordinationReport(_logger, Module, CoordinationPrimitive.AtomicOperations)));
    }
}

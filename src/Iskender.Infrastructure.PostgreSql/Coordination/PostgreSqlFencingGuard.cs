using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// The fencing guard on <c>isk_coordination.fences</c> (FR-017, contract Leases and fencing rule 6, research R8). It runs
/// one statement in the caller's unit, which both checks and raises the resource's highest token, so the row lock it takes
/// orders the caller's protected write behind every earlier one until the unit ends. A stale token, or one from another
/// lease key, faults the unit, so it commits nothing. It accepts only PostgreSQL leases, whose tokens survive a restart.
/// It takes no advisory lock and no session-scoped lock (FR-018). Owned by ISK-0005.
/// </summary>
public sealed class PostgreSqlFencingGuard : IFencingGuard
{
    private const string AcceptSql =
        "INSERT INTO isk_coordination.fences AS f (scope_key, resource_hash, lease_hash, highest_token) "
        + "VALUES ($1, $2, $3, $4) "
        + "ON CONFLICT (scope_key, resource_hash) DO UPDATE SET highest_token = excluded.highest_token "
        + "WHERE f.lease_hash = excluded.lease_hash AND f.highest_token <= excluded.highest_token "
        + "RETURNING 1";

    private const string FencedWithSql =
        "SELECT lease_hash FROM isk_coordination.fences WHERE {scope} AND resource_hash = $2";

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlFencingGuard(ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken cancellationToken)
    {
        if (unit is null || resource is null || lease is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (lease.Issuer != CoordinatorKind.PostgreSql)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationCapabilityUnsupported, CoordinationFailureClass.CapabilityUnsupported);
        }

        if (!unit.Scope.Owner.Equals(lease.Scope.Owner))
        {
            throw new GatewayException(ErrorCodes.CoordinationScopeRequired);
        }

        var adapter = PostgreSqlUnitOfWork.From(unit);
        var resourceHash = CoordinationKey.Hash(lease.Scope.Owner, resource.Module, resource.Name);
        var leaseHash = CoordinationKey.Hash(lease.Scope.Owner, lease.Name.Module, lease.Name.Name);
        await CoordinationTelemetry.MeasureAsync(resource.Module, CoordinationPrimitive.Fencing, "accept", CoordinatorKind.PostgreSql, _timeProvider, async () =>
        {
            try
            {
                var accepted = await adapter.Command(CoordinationTables.Fences, AcceptSql)
                    .With(resourceHash).With(leaseHash).With(lease.Token.Value)
                    .QueryAsync(reader => reader.GetInt32(0), cancellationToken);
                if (accepted.Count > 0)
                {
                    return true;
                }

                var fencedWith = await adapter.Command(CoordinationTables.Fences, FencedWithSql)
                    .With(resourceHash)
                    .QueryAsync(reader => reader.GetString(0), cancellationToken);
                var reason = fencedWith.Count > 0 && !string.Equals(fencedWith[0], leaseHash, StringComparison.Ordinal) ? "other_lease" : "stale_token";
                CoordinationLog.LogFencingRejected(_logger, resource.Module, reason);
                CoordinationTelemetry.RecordFencingRejected(resource.Module);
            }
            catch (GatewayException failure)
            {
                throw CoordinationUnits.Map(failure, unit.Failure);
            }

            // Faulting the unit is what makes it commit nothing, whatever the caller does next.
            _ = adapter.Fault(new Classification(new PersistenceFailure(ErrorCodes.CoordinationFencingRejected, PersistenceFailureClass.ConcurrencyConflict), SqlStateClass: null));
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationFencingRejected, CoordinationFailureClass.FencingRejected);
        });
    }
}

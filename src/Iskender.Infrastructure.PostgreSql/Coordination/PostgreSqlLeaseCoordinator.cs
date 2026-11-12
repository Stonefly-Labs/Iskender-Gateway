using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// Leases and fencing tokens in <c>isk_coordination.leases</c> (FR-002a, FR-014 to FR-016, research R8 and R12). Each
/// operation is one statement in its own unit of work: acquire takes an absent or expired row with a token from
/// <c>isk_coordination.fencing_tokens</c>, renew extends only the caller's unexpired row, and release deletes only the
/// caller's row. Expiry is the server's <c>clock_timestamp()</c>, and every grant returns the time left on that clock.
/// The row lock serializes callers and the sequence survives a restart, so it declares <c>strict</c>. It never takes an
/// advisory lock (FR-018). Owned by ISK-0005.
/// </summary>
public sealed class PostgreSqlLeaseCoordinator : ILeaseCoordinator
{
    private const string AcquireSql =
        "INSERT INTO isk_coordination.leases AS l (scope_key, key_hash, holder, token, expires_at) "
        + "VALUES ($1, $2, $3, nextval('isk_coordination.fencing_tokens'), clock_timestamp() + $4) "
        + "ON CONFLICT (scope_key, key_hash) DO UPDATE SET holder = excluded.holder, token = excluded.token, expires_at = excluded.expires_at "
        + "WHERE l.expires_at <= clock_timestamp() "
        + "RETURNING token, expires_at - clock_timestamp()";

    private const string RenewSql =
        "UPDATE isk_coordination.leases SET expires_at = clock_timestamp() + $4 "
        + "WHERE {scope} AND key_hash = $2 AND holder = $3 AND token = $5 AND expires_at > clock_timestamp() "
        + "RETURNING expires_at - clock_timestamp()";

    private const string ReleaseSql =
        "DELETE FROM isk_coordination.leases WHERE {scope} AND key_hash = $2 AND holder = $3 AND token = $4";

    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlLeaseCoordinator(IUnitOfWorkFactory units, CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>What this coordinator declares: <c>strict</c> for every primitive.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.PostgreSql;

    public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireDuration(_settings, duration);
        LeasePolicy.RequireWait(_settings, wait);
        var key = CoordinationKey.Hash(scope.Owner, name.Module, name.Name);
        return Measure(name.Module, "acquire", () => LeasePolicy.AcquireAsync(
            scope,
            wait,
            _timeProvider,
            async token =>
            {
                var holder = Guid.NewGuid();
                var sent = _timeProvider.GetTimestamp();
                var granted = await CoordinationUnits.RunAsync(
                    _units,
                    scope,
                    "leases.acquire",
                    _settings.OperationTimeout,
                    async (unit, inner) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Leases, AcquireSql)
                        .With(key).With(holder).With(duration)
                        .QueryAsync(reader => (Token: reader.GetInt64(0), Remaining: reader.GetFieldValue<TimeSpan>(1)), inner),
                    token,
                    Report(name.Module));
                return granted.Count == 0
                    ? LeaseAcquisition.Contended
                    : LeaseAcquisition.Acquired(Lease.Grant(scope, name, new FencingToken(granted[0].Token), holder, CoordinatorKind.PostgreSql, granted[0].Remaining, sent, _settings.SafetyMargin, _timeProvider));
            },
            cancellationToken));
    }

    public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireDuration(_settings, duration);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        return Measure(lease.Name.Module, "renew", async () =>
        {
            var sent = _timeProvider.GetTimestamp();
            var renewed = await CoordinationUnits.RunAsync(
                _units,
                lease.Scope,
                "leases.renew",
                _settings.OperationTimeout,
                async (unit, inner) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Leases, RenewSql)
                    .With(Key(lease)).With(lease.Holder).With(duration).With(lease.Token.Value)
                    .QueryAsync(reader => reader.GetFieldValue<TimeSpan>(0), inner),
                cancellationToken,
                Report(lease.Name.Module));
            if (renewed.Count == 0)
            {
                CoordinationLog.LogLeaseLost(_logger, lease.Name.Module, CoordinatorKind.PostgreSql);
                CoordinationTelemetry.RecordLeaseLost(lease.Name.Module, CoordinatorKind.PostgreSql);
                throw CoordinationFailures.Raise(ErrorCodes.CoordinationLeaseLost, CoordinationFailureClass.LeaseLost);
            }

            return Lease.Grant(lease.Scope, lease.Name, lease.Token, lease.Holder, CoordinatorKind.PostgreSql, renewed[0], sent, _settings.SafetyMargin, _timeProvider);
        });
    }

    public async ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        await Measure(lease.Name.Module, "release", async () => await CoordinationUnits.RunAsync(
            _units,
            lease.Scope,
            "leases.release",
            _settings.OperationTimeout,
            async (unit, inner) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Leases, ReleaseSql)
                .With(Key(lease)).With(lease.Holder).With(lease.Token.Value)
                .ExecuteAsync(inner),
            cancellationToken,
            Report(lease.Name.Module)));
    }

    private CoordinationReport Report(string module) => new(_logger, module, CoordinationPrimitive.Leases);

    private static string Key(Lease lease) => CoordinationKey.Hash(lease.Scope.Owner, lease.Name.Module, lease.Name.Name);

    // A lease from another coordinator has no row here to renew or release.
    private static void RequireOwn(Lease lease)
    {
        if (lease is null || lease.Issuer != CoordinatorKind.PostgreSql)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    private ValueTask<T> Measure<T>(string module, string operation, Func<ValueTask<T>> work) =>
        CoordinationTelemetry.MeasureAsync(module, CoordinationPrimitive.Leases, operation, CoordinatorKind.PostgreSql, _timeProvider, work);
}

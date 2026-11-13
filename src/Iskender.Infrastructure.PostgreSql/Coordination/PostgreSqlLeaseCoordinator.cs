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
/// The row lock serializes callers and the sequence survives a restart, so it declares <c>strict</c>. An acquire cancelled
/// while its commit's outcome is unknown is undone by releasing its holder, so a grant the server committed is not left
/// held. It never takes an advisory lock (FR-018). Owned by ISK-0005.
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

    // An upsert, not a delete: a delete cannot see a row whose acquire is still committing and would remove nothing, but
    // the insert waits on the key for that commit to settle. A committed grant to this holder is then expired, and a rolled
    // back one leaves an expired row of this holder, which counts as absent; another holder's row is never touched.
    private const string ReleaseHolderSql =
        "INSERT INTO isk_coordination.leases AS l (scope_key, key_hash, holder, token, expires_at) "
        + "VALUES ($1, $2, $3, 0, clock_timestamp()) "
        + "ON CONFLICT (scope_key, key_hash) DO UPDATE SET expires_at = clock_timestamp() "
        + "WHERE l.holder = excluded.holder";

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
                IReadOnlyList<(long Token, TimeSpan Remaining)> granted;
                try
                {
                    granted = await CoordinationUnits.RunAsync(
                        _units,
                        scope,
                        "leases.acquire",
                        _settings.OperationTimeout,
                        async (unit, inner) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Leases, AcquireSql)
                            .With(key).With(holder).With(duration)
                            .QueryAsync(reader => (Token: reader.GetInt64(0), Remaining: reader.GetFieldValue<TimeSpan>(1)), inner),
                        token,
                        Report(name.Module));
                }
                catch (GatewayException failure) when (CoordinationFailures.IsOutcomeUnknown(failure)
                    && (cancellationToken.IsCancellationRequested || scope.RequestCancellation.IsCancellationRequested))
                {
                    // ISK-0004 reports every unknown commit outcome as unavailable; a fired caller or request token is a
                    // cancellation (contract Failures rule 4), and the spec's Edge Cases leave no lease held afterwards.
                    await ReleaseHolderAsync(scope, name.Module, key, holder);
                    throw CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled, outcomeUnknown: true);
                }
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

    // The commit of the acquire whose outcome is unknown may have granted it. Deleting by holder undoes that grant and never
    // touches another holder's lease. It runs detached from the stopped request, bounded by operationTimeout; if it fails
    // too, the lease expires at its duration.
    private async ValueTask ReleaseHolderAsync(CoordinationScope scope, string module, string key, Guid holder)
    {
        try
        {
            await CoordinationUnits.RunAsync(
                _units,
                scope,
                "leases.release_holder",
                _settings.OperationTimeout,
                async (unit, inner) => await PostgreSqlUnitOfWork.From(unit).Command(CoordinationTables.Leases, ReleaseHolderSql)
                    .With(key).With(holder)
                    .ExecuteAsync(inner),
                CancellationToken.None,
                Report(module),
                detached: true);
        }
        catch (GatewayException)
        {
            // Already classified and reported by the unit; the caller still sees coordination.cancelled.
        }
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

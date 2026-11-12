using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// Atomic operations on <c>isk_coordination.coordination_values</c> (FR-011 to FR-013, research R8). Each operation runs
/// one statement that both checks and changes the row, in its own unit, so the row lock serializes concurrent callers and
/// every primitive is <c>strict</c>. An expired row counts as absent and is replaced by the next write to its key. Overflow
/// is checked in SQL, so no statement raises an arithmetic error. Time is the server's <c>clock_timestamp()</c>. Owned by
/// ISK-0005.
/// </summary>
public sealed class PostgreSqlAtomicOperations : IAtomicOperations
{
    // Shared by every write: an expired row restarts at version 1 with the new expiry; a live one keeps its expiry.
    private const string Replace =
        "version = CASE WHEN c.expires_at <= clock_timestamp() THEN 1 ELSE c.version + 1 END, "
        + "expires_at = CASE WHEN c.expires_at <= clock_timestamp() THEN excluded.expires_at ELSE c.expires_at END";

    private const string AddSql =
        "INSERT INTO isk_coordination.coordination_values AS c (scope_key, key_hash, value, version, expires_at) "
        + "VALUES ($1, $2, $3, 1, clock_timestamp() + $4) "
        + "ON CONFLICT (scope_key, key_hash) DO UPDATE SET "
        + "value = CASE WHEN c.expires_at <= clock_timestamp() THEN excluded.value ELSE c.value + excluded.value END, " + Replace + " "
        + "WHERE c.expires_at <= clock_timestamp() OR c.value::numeric + excluded.value BETWEEN -9223372036854775808 AND 9223372036854775807 "
        + "RETURNING value, version";

    private const string ReserveSql =
        "INSERT INTO isk_coordination.coordination_values AS c (scope_key, key_hash, value, version, expires_at) "
        + "SELECT $1, $2, $3, 1, clock_timestamp() + $4 WHERE $3 <= $5 "
        + "ON CONFLICT (scope_key, key_hash) DO UPDATE SET "
        + "value = CASE WHEN c.expires_at <= clock_timestamp() THEN excluded.value ELSE c.value + excluded.value END, " + Replace + " "
        + "WHERE c.expires_at <= clock_timestamp() OR c.value <= $5 - excluded.value "
        + "RETURNING value";

    private const string ReleaseSql =
        "UPDATE isk_coordination.coordination_values SET value = CASE WHEN value <= $3 THEN 0 ELSE value - $3 END, version = version + 1 "
        + "WHERE {scope} AND key_hash = $2 AND expires_at > clock_timestamp() "
        + "RETURNING value, version";

    private const string CreateSql =
        "INSERT INTO isk_coordination.coordination_values AS c (scope_key, key_hash, value, version, expires_at) "
        + "VALUES ($1, $2, $3, 1, clock_timestamp() + $4) "
        + "ON CONFLICT (scope_key, key_hash) DO UPDATE SET value = excluded.value, " + Replace + " "
        + "WHERE c.expires_at <= clock_timestamp() "
        + "RETURNING 1";

    private const string SetSql =
        "UPDATE isk_coordination.coordination_values SET value = $4, version = version + 1 "
        + "WHERE {scope} AND key_hash = $2 AND version = $3 AND expires_at > clock_timestamp() "
        + "RETURNING 1";

    private const string ReadSql =
        "SELECT value, version FROM isk_coordination.coordination_values WHERE {scope} AND key_hash = $2 AND expires_at > clock_timestamp()";

    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationSettings _settings;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlAtomicOperations(IUnitOfWorkFactory units, CoordinationSettings settings, TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>What this coordinator declares: <c>strict</c> for every primitive.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.PostgreSql;

    public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Run(scope, name, "add", async (unit, key, token) =>
        {
            var rows = await unit.Command(CoordinationTables.Values, AddSql).With(key).With(delta).With(expiry)
                .QueryAsync(reader => new CoordinationValue(reader.GetInt64(0), reader.GetInt64(1)), token);
            return rows.Count > 0 ? rows[0] : throw new GatewayException(ErrorCodes.ValueInvalid);
        }, cancellationToken);
    }

    public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        AtomicPolicy.RequireLimit(limit);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Run(scope, name, "reserve", async (unit, key, token) =>
        {
            var granted = await unit.Command(CoordinationTables.Values, ReserveSql).With(key).With(amount).With(expiry).With(limit)
                .QueryAsync(reader => reader.GetInt64(0), token);
            if (granted.Count > 0)
            {
                return new Reservation(true, granted[0]);
            }

            // Denied, so nothing changed; the value reported is the one read now.
            var current = await ReadAsync(unit, key, token);
            return new Reservation(false, current?.Value ?? 0);
        }, cancellationToken);
    }

    public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Run(scope, name, "release", async (unit, key, token) =>
        {
            var rows = await unit.Command(CoordinationTables.Values, ReleaseSql).With(key).With(amount)
                .QueryAsync(reader => new CoordinationValue(reader.GetInt64(0), reader.GetInt64(1)), token);
            return rows.Count > 0 ? rows[0] : null;
        }, cancellationToken);
    }

    public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Run(scope, name, "compare_and_set", async (unit, key, token) =>
        {
            var command = expectedVersion == 0
                ? unit.Command(CoordinationTables.Values, CreateSql).With(key).With(value).With(expiry)
                : unit.Command(CoordinationTables.Values, SetSql).With(key).With(expectedVersion).With(value);
            return (await command.QueryAsync(reader => reader.GetInt32(0), token)).Count > 0;
        }, cancellationToken);
    }

    public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Run(scope, name, "read", (unit, key, token) => ReadAsync(unit, key, token), cancellationToken);
    }

    private static async ValueTask<CoordinationValue?> ReadAsync(PostgreSqlUnitOfWork unit, string key, CancellationToken cancellationToken)
    {
        var rows = await unit.Command(CoordinationTables.Values, ReadSql).With(key)
            .QueryAsync(reader => new CoordinationValue(reader.GetInt64(0), reader.GetInt64(1)), cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    private ValueTask<T> Run<T>(CoordinationScope scope, CoordinationName name, string operation, Func<PostgreSqlUnitOfWork, string, CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken)
    {
        var key = CoordinationKey.Hash(scope.Owner, name.Module, name.Name);
        return CoordinationTelemetry.MeasureAsync(name.Module, CoordinationPrimitive.AtomicOperations, operation, CoordinatorKind.PostgreSql, _timeProvider, () =>
            CoordinationUnits.RunAsync(
                _units,
                scope,
                "atomic." + operation,
                _settings.OperationTimeout,
                (unit, token) => work(PostgreSqlUnitOfWork.From(unit), key, token),
                cancellationToken));
    }
}

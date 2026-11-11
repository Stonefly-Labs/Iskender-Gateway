using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Health;

/// <summary>
/// Reports whether the store answers and its schema suits this release (FR-025, contract Health rules, research R13). On
/// the runtime data source, within <c>healthProbeTimeout</c>, it runs <c>SELECT 1</c>, then one round trip reading
/// <c>pg_locks</c>, the history, and the runtime role's <c>USAGE</c> on each known module schema the history shows,
/// and takes the first matching reason. It never throws for a database condition, and the caller's token firing throws
/// <c>persistence.cancelled</c>. Event 4013 is logged when the reason differs from the last result. Owned by ISK-0004.
/// </summary>
public sealed class PostgreSqlHealthProbe : IPersistenceHealthProbe
{
    // Advisory locks belong to one database, so a migration of another database on the same server does not count.
    private const string LockHeldSql =
        "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND granted AND classid::bigint = $1 AND objid::bigint = $2 AND objsubid = 1 " +
        "AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))";

    private const string HistoryExistsSql = "SELECT to_regclass('isk_migrations.history') IS NOT NULL";

    private const string ReadHistorySql = "SELECT module, version, checksum FROM isk_migrations.history";

    private const string PrivilegeSql =
        "SELECT known.module, has_schema_privilege(current_user, known.schema, 'USAGE') " +
        "FROM unnest($1::text[], $2::text[]) AS known (module, schema) WHERE known.module IN (SELECT module FROM isk_migrations.history)";

    private readonly PostgreSqlDataSources _sources;
    private readonly MigrationSet _migrations;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private int _lastReason = -1;

    public PostgreSqlHealthProbe(PostgreSqlDataSources sources, MigrationSet migrations, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _sources = sources ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _migrations = migrations ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Test seam: the history read, so a test can make it fail with an SQLSTATE no other reason describes.</summary>
    internal string HistoryQuery { get; set; } = ReadHistorySql;

    public async ValueTask<PersistenceHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new GatewayException(ErrorCodes.PersistenceCancelled);
        }

        var result = await ProbeAsync(cancellationToken);
        var previous = Interlocked.Exchange(ref _lastReason, (int)result.Reason);
        if (previous != (int)result.Reason)
        {
            PersistenceLog.LogHealthChanged(_logger, result, previous < 0 ? null : (PersistenceHealthReason)previous);
        }

        return result;
    }

    private async Task<PersistenceHealthResult> ProbeAsync(CancellationToken cancellationToken)
    {
        using var bound = new CancellationTokenSource(_sources.Settings.HealthProbeTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        var work = QueryAsync(linked.Token);
        try
        {
            // Npgsql's open does not watch the token while it waits for the server's startup reply, so the probe waits on
            // the token itself. Abandoned work ends at the connection timeout, disposes its connection, and is observed.
            return Decide(await work.WaitAsync(linked.Token));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new GatewayException(ErrorCodes.PersistenceCancelled);
        }
        catch (Exception exception)
        {
            // Any failure is a reason, never an exception: the driver's text and SQLSTATE stay here (FR-026).
            if (bound.IsCancellationRequested)
            {
                return PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Timeout);
            }

            return PersistenceHealthResult.Unhealthy(FailureClassifier.Classify(exception, FailureSite.Statement, FiredToken.None).Failure.FailureClass switch
            {
                PersistenceFailureClass.Unavailable => PersistenceHealthReason.Unreachable,
                PersistenceFailureClass.AuthenticationFailed => PersistenceHealthReason.AuthenticationFailed,
                PersistenceFailureClass.Timeout => PersistenceHealthReason.Timeout,
                _ => PersistenceHealthReason.Unexpected,
            });
        }
        finally
        {
            if (!work.IsCompleted)
            {
                _ = work.ContinueWith(abandoned => _ = abandoned.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
        }
    }

    private async Task<Observation> QueryAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _sources.Runtime.OpenConnectionAsync(cancellationToken);
        await using (var ping = new NpgsqlCommand("SELECT 1", connection))
        {
            await ping.ExecuteScalarAsync(cancellationToken);
        }

        return await ReadAsync(connection, cancellationToken);
    }

    // One round trip. A failing history read ends the batch, but the lock and history-exists results before it still arrive.
    private async Task<Observation> ReadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var modules = _migrations.Modules;
        await using var batch = new NpgsqlBatch(connection)
        {
            BatchCommands =
            {
                new NpgsqlBatchCommand(LockHeldSql) { Parameters = { new() { Value = (long)unchecked((uint)(AdvisoryLockKey.Migration >> 32)) }, new() { Value = (long)unchecked((uint)AdvisoryLockKey.Migration) } } },
                new NpgsqlBatchCommand(HistoryExistsSql),
                new NpgsqlBatchCommand(HistoryQuery),
                new NpgsqlBatchCommand(PrivilegeSql) { Parameters = { new() { Value = modules.Select(module => module.Name).ToArray() }, new() { Value = modules.Select(module => module.Schema).ToArray() } } },
            },
        };

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var observation = new Observation { LockHeld = reader.GetBoolean(0) };
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        observation.HistoryExists = reader.GetBoolean(0);
        try
        {
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                observation.History.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
            }

            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                observation.MissingUsage |= !reader.GetBoolean(1);
            }
        }
        catch (PostgresException failure) when (!cancellationToken.IsCancellationRequested)
        {
            observation.HistoryFailure = failure.SqlState;
        }

        return observation;
    }

    private PersistenceHealthResult Decide(Observation observed)
    {
        var reason = observed switch
        {
            { LockHeld: true } => PersistenceHealthReason.MigrationInProgress,
            { HistoryExists: false } => PersistenceHealthReason.SchemaBehind,
            { HistoryFailure: "42501" } or { MissingUsage: true } => PersistenceHealthReason.InsufficientPrivilege,
            { HistoryFailure: not null } => PersistenceHealthReason.Unexpected,
            _ => DecideSchema(observed.History),
        };
        return reason == PersistenceHealthReason.Ok ? PersistenceHealthResult.Healthy : PersistenceHealthResult.Unhealthy(reason);
    }

    // Unknown applied migrations are ignored here; the runner still refuses them (clarification of 2026-11-09).
    private PersistenceHealthReason DecideSchema(List<(string Module, int Version, string Checksum)> history)
    {
        if (history.Any(row => _migrations.TryGetScript(row.Module, row.Version, out var script) && !string.Equals(script.Checksum, row.Checksum, StringComparison.Ordinal)))
        {
            return PersistenceHealthReason.ChecksumMismatch;
        }

        var versions = _migrations.Modules.Select(module => (module.SupportedRange, Version: history.Where(row => row.Module == module.Name).Select(row => row.Version).DefaultIfEmpty(0).Max())).ToList();
        if (versions.Any(entry => entry.Version < entry.SupportedRange.Minimum))
        {
            return PersistenceHealthReason.SchemaBehind;
        }

        return versions.Any(entry => entry.Version > entry.SupportedRange.Maximum) ? PersistenceHealthReason.SchemaAhead : PersistenceHealthReason.Ok;
    }

    private sealed class Observation
    {
        public bool LockHeld { get; init; }

        public bool HistoryExists { get; set; }

        public string? HistoryFailure { get; set; }

        public bool MissingUsage { get; set; }

        public List<(string Module, int Version, string Checksum)> History { get; } = [];
    }
}

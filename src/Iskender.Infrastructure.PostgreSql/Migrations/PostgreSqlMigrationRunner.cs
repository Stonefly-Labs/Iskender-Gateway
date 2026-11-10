using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// Applies and reverses a release's migrations under the migration identity only (FR-017 to FR-023, contract
/// Migrations rules, research R11, R12). Each run opens the migration data source, checks that the group role
/// <c>iskender_runtime</c> exists, takes the session migration lock, creates <c>isk_migrations.history</c> if missing,
/// and releases the lock at the end. Each migration runs in its own transaction with <c>search_path</c> set to its
/// module's schema only, and a failure rolls back that migration alone. It runs only when called (FR-021). Owned by
/// ISK-0004.
/// </summary>
public sealed class PostgreSqlMigrationRunner : IMigrationRunner
{
    private const string Framework = "migrations";

    private const string RoleExistsSql = "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1)";

    private const string HistorySql =
        "CREATE SCHEMA IF NOT EXISTS isk_migrations; " +
        "CREATE TABLE IF NOT EXISTS isk_migrations.history (module text NOT NULL, version integer NOT NULL, checksum text NOT NULL, " +
        "applied_at timestamptz NOT NULL, applied_by text NOT NULL, PRIMARY KEY (module, version)); " +
        "GRANT USAGE ON SCHEMA isk_migrations TO iskender_runtime; " +
        "GRANT SELECT ON isk_migrations.history TO iskender_runtime";

    private const string ReadHistorySql = "SELECT module, version, checksum FROM isk_migrations.history ORDER BY module, version";

    private const string SchemaExistsSql = "SELECT to_regnamespace($1) IS NOT NULL";

    private const string SearchPathSql = "SELECT set_config('search_path', $1, true)";

    private const string RecordSql = "INSERT INTO isk_migrations.history (module, version, checksum, applied_at, applied_by) VALUES ($1, $2, $3, now(), current_user)";

    // Tenant tables, found by their owner column, that lack forced row-level security, a policy, or a project policy naming owner_project_id (research R5).
    private const string RowSecurityGapsSql =
        "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE n.nspname LIKE 'isk\\_%' AND n.nspname <> 'isk_migrations' AND c.relkind IN ('r', 'p') " +
        "AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'owner_organization_id' AND NOT a.attisdropped) " +
        "AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity " +
        "OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid) " +
        "OR (EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'owner_project_id' AND NOT a.attisdropped) " +
        "AND NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid " +
        "AND coalesce(pg_get_expr(p.polqual, p.polrelid), '') || ' ' || coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '') LIKE '%owner_project_id%')))";

    private readonly PostgreSqlDataSources _sources;
    private readonly MigrationSet _set;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public PostgreSqlMigrationRunner(PostgreSqlDataSources sources, MigrationSet set, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _sources = sources ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _set = set ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Test seam: the group role checked before the lock, so a test can prove the refusal without dropping the shared role.</summary>
    internal string RuntimeRole { get; set; } = "iskender_runtime";

    /// <summary>Test seam: runs while the lock is held, before any migration, so a test can observe the lock.</summary>
    internal Func<CancellationToken, Task>? WhileLocked { get; set; }

    public ValueTask<MigrationRunResult> ApplyAsync(MigrationApplyRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return RunAsync("apply", ApplyPendingAsync, cancellationToken);
    }

    public ValueTask<MigrationRunResult> ReverseAsync(MigrationReverseRequest request, CancellationToken cancellationToken) =>
        throw new GatewayException(ErrorCodes.Internal);

    public ValueTask<IReadOnlyList<MigrationStatus>> GetStatusAsync(CancellationToken cancellationToken) =>
        throw new GatewayException(ErrorCodes.Internal);

    // One run: connect, check the role, take the lock, ensure the history, do the work, release the lock (events 4007, 4011).
    private async ValueTask<T> RunAsync<T>(string operation, Func<NpgsqlConnection, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        PersistenceLog.LogMigrationStarted(_logger, operation);
        var outcome = "failed";
        try
        {
            var source = _sources.Migration;
            await using var connection = await Driver(operation, () => source.OpenConnectionAsync(cancellationToken).AsTask(), cancellationToken);
            if (!await Driver(operation, () => Scalar<bool>(connection, null, RoleExistsSql, [RuntimeRole], cancellationToken), cancellationToken))
            {
                throw MigrationRefusal.Refuse(_logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.MissingRuntimeRole);
            }

            MigrationLock held;
            try
            {
                held = await Driver(operation, () => MigrationLock.AcquireAsync(connection, _sources.Settings.MigrationLockWait, _timeProvider, _logger, operation, cancellationToken), cancellationToken);
            }
            catch (GatewayException failure) when (failure.Code == ErrorCodes.PersistenceMigrationLocked)
            {
                outcome = "locked";
                throw;
            }

            await using (held)
            {
                await Driver(operation, () => Execute(connection, null, HistorySql, [], cancellationToken), cancellationToken);
                if (WhileLocked is { } observe)
                {
                    await observe(cancellationToken);
                }

                var result = await work(connection, cancellationToken);
                outcome = "succeeded";
                return result;
            }
        }
        finally
        {
            PersistenceLog.LogMigrationCompleted(_logger, operation, outcome, _timeProvider.GetElapsedTime(started));
        }
    }

    // Refuses an unknown or edited applied migration before changing anything, then applies the rest in global order.
    private async Task<MigrationRunResult> ApplyPendingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var applied = await Driver("apply", () => ReadHistoryAsync(connection, cancellationToken), cancellationToken);
        foreach (var row in applied)
        {
            if (!_set.TryGetScript(row.Module, row.Version, out var script))
            {
                throw MigrationRefusal.Refuse(_logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.UnknownAppliedMigration, row.Module, row.Version);
            }

            if (!string.Equals(script.Checksum, row.Checksum, StringComparison.Ordinal))
            {
                throw MigrationRefusal.Refuse(_logger, ErrorCodes.PersistenceMigrationChecksumMismatch, MigrationRefusal.ChecksumMismatch, row.Module, row.Version);
            }
        }

        var done = applied.Select(row => (row.Module, row.Version)).ToHashSet();
        var pending = _set.Migrations.Where(script => !done.Contains((script.Identity.Module, script.Identity.Version))).ToList();
        var result = new List<MigrationIdentity>(pending.Count);
        for (var i = 0; i < pending.Count; i++)
        {
            var lastOfModule = i + 1 == pending.Count || pending[i + 1].Identity.Module != pending[i].Identity.Module;
            await ApplyStepAsync(connection, pending[i], lastOfModule, cancellationToken);
            result.Add(pending[i].Identity);
        }

        return new MigrationRunResult(result, []);
    }

    // One migration in its own transaction: the module schema if missing, the forward script, its history row, and,
    // after the module's last pending migration, the row-level security check (events 4009, 4010).
    private async Task ApplyStepAsync(NpgsqlConnection connection, MigrationScript script, bool checkRowSecurity, CancellationToken cancellationToken)
    {
        const string operation = "apply";
        var migration = script.Identity;
        _set.TryGetModule(migration.Module, out var module);
        var started = _timeProvider.GetTimestamp();
        using var activity = PersistenceTelemetry.StartMigrationStep(migration, operation);
        try
        {
            await using var transaction = await Driver(operation, () => connection.BeginTransactionAsync(cancellationToken).AsTask(), cancellationToken);
            await Driver(operation, () => CreateSchemaIfMissingAsync(connection, transaction, module.Schema, cancellationToken), cancellationToken);
            await Driver(operation, () => Execute(connection, transaction, SearchPathSql, [module.Schema], cancellationToken), cancellationToken);
            try
            {
                await Execute(connection, transaction, script.Forward, [], cancellationToken);
            }
            catch (Exception exception) when (exception is not GatewayException)
            {
                throw StepFailed(operation, migration, exception, MigrationRefusal.ForwardFailed, cancellationToken);
            }

            await Driver(operation, () => Execute(connection, transaction, RecordSql, [migration.Module, migration.Version, script.Checksum], cancellationToken), cancellationToken);
            if (checkRowSecurity && await Driver(operation, () => Scalar<long>(connection, transaction, RowSecurityGapsSql, [], cancellationToken), cancellationToken) > 0)
            {
                throw MigrationRefusal.Refuse(_logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.MissingRowSecurity, migration.Module, migration.Version);
            }

            await Driver(operation, () => transaction.CommitAsync(cancellationToken), cancellationToken);
        }
        catch (GatewayException)
        {
            PersistenceTelemetry.End(activity, "failed");
            throw;
        }

        PersistenceLog.LogMigrationStepApplied(_logger, migration, operation, _timeProvider.GetElapsedTime(started));
        PersistenceTelemetry.End(activity, "succeeded");
    }

    // Created inside the first pending migration's transaction, so a failed first migration leaves no schema (research R12).
    private static async Task CreateSchemaIfMissingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken cancellationToken)
    {
        if (await Scalar<bool>(connection, transaction, SchemaExistsSql, [schema], cancellationToken))
        {
            return;
        }

        await Execute(
            connection,
            transaction,
            $"CREATE SCHEMA {schema}; GRANT USAGE ON SCHEMA {schema} TO iskender_runtime; " +
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO iskender_runtime; " +
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT USAGE ON SEQUENCES TO iskender_runtime",
            [],
            cancellationToken);
    }

    private static async Task<List<(string Module, int Version, string Checksum)>> ReadHistoryAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadHistorySql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(string, int, string)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
        }

        return rows;
    }

    // A failed script: the code from the classifier, the module, version, and safe reason as diagnostics values (event 4010).
    private GatewayException StepFailed(string operation, MigrationIdentity migration, Exception exception, string reason, CancellationToken cancellationToken)
    {
        var classification = FailureClassifier.Classify(exception, FailureSite.Statement, cancellationToken.IsCancellationRequested ? FiredToken.Cancellation : FiredToken.None);
        if (classification.Failure.FailureClass == PersistenceFailureClass.Unexpected)
        {
            PersistenceLog.LogUnexpectedFailure(_logger, Framework, operation, classification.SqlStateClass);
        }

        PersistenceLog.LogMigrationStepFailed(_logger, migration, classification.Failure.FailureClass, reason);
        return MigrationRefusal.Failure(classification.Failure.Code, reason, migration.Module, migration.Version);
    }

    // Runs framework I/O, turning a driver failure into its classified code with no inner exception or server text.
    private async Task<T> Driver<T>(string operation, Func<Task<T>> io, CancellationToken cancellationToken)
    {
        try
        {
            return await io();
        }
        catch (Exception exception) when (exception is not GatewayException)
        {
            throw Classified(operation, exception, cancellationToken);
        }
    }

    private async Task Driver(string operation, Func<Task> io, CancellationToken cancellationToken)
    {
        try
        {
            await io();
        }
        catch (Exception exception) when (exception is not GatewayException)
        {
            throw Classified(operation, exception, cancellationToken);
        }
    }

    private GatewayException Classified(string operation, Exception exception, CancellationToken cancellationToken)
    {
        var classification = FailureClassifier.Classify(exception, FailureSite.Statement, cancellationToken.IsCancellationRequested ? FiredToken.Cancellation : FiredToken.None);
        if (classification.Failure.FailureClass == PersistenceFailureClass.Unexpected)
        {
            PersistenceLog.LogUnexpectedFailure(_logger, Framework, operation, classification.SqlStateClass);
        }

        return classification.ToException();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, object[] parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Positional parameters only ($1, $2, ...). A script with no parameters may hold several statements.
    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}

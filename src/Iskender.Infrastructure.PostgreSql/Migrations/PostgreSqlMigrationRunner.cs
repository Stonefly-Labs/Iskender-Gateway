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
/// and releases the lock at the end. It runs only when called (FR-021). Owned by ISK-0004.
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

        return RunAsync("apply", (_, _) => Task.FromResult(new MigrationRunResult([], [])), cancellationToken);
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
            if (!await Driver(operation, () => Scalar<bool>(connection, RoleExistsSql, RuntimeRole, cancellationToken), cancellationToken))
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
                await Driver(operation, () => Execute(connection, null, HistorySql, cancellationToken), cancellationToken);
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

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, object parameter, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new() { Value = parameter } } };
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// One scoped transaction on one connection (FR-004, FR-011, research R4, R5, R7, R9). It commits only on
/// <see cref="CommitAsync"/>, and disposing it otherwise rolls it back and returns the connection to the pool. It serves
/// one operation at a time. Every failure records <see cref="Failure"/>, faults the unit, and throws a
/// <see cref="GatewayException"/> carrying only the code. Owned by ISK-0004.
/// </summary>
public sealed class PostgreSqlUnitOfWork : IUnitOfWork
{
    private readonly UnitOfWorkOptions _options;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly UnitBounds _bounds;
    private readonly Activity? _activity;
    private readonly long _started;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private int _busy;
    private int _statements;
    private int _state = (int)UnitOfWorkState.Open;
    private PersistenceFailure? _failure;

    internal PostgreSqlUnitOfWork(
        UnitOfWorkScope scope,
        UnitOfWorkOptions options,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UnitBounds bounds,
        Activity? activity,
        long started,
        PostgreSqlSettings settings,
        TimeProvider timeProvider,
        ILogger logger)
    {
        Scope = scope;
        _options = options;
        _connection = connection;
        _transaction = transaction;
        _bounds = bounds;
        _activity = activity;
        _started = started;
        Settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public UnitOfWorkScope Scope { get; }

    public UnitOfWorkIsolation Isolation => _options.Isolation;

    public UnitOfWorkState State => (UnitOfWorkState)Volatile.Read(ref _state);

    public PersistenceFailure? Failure => Volatile.Read(ref _failure);

    internal PostgreSqlSettings Settings { get; }

    internal string Module => _options.Module;

    internal string Operation => _options.Operation;

    /// <summary>Test seam: false turns the scope pre-check off, so the bypass tests can prove row-level security alone (research R6).</summary>
    internal bool ScopePreCheck { get; set; } = true;

    /// <summary>Test seam: runs as the commit starts, so a test can break the connection during <c>COMMIT</c> (FR-015).</summary>
    internal Func<Task>? DuringCommit { get; set; }

    /// <summary>How many statements this unit has sent, so a test can prove a refusal sent none.</summary>
    internal int StatementsSent => Volatile.Read(ref _statements);

    /// <summary>The adapter type behind a port, for repository code. Any other implementation fails with <c>core.internal</c>.</summary>
    internal static PostgreSqlUnitOfWork From(IUnitOfWork unit) => unit as PostgreSqlUnitOfWork ?? throw new GatewayException(ErrorCodes.Internal);

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async (_, token) =>
            {
                if (DuringCommit is { } breakConnection)
                {
                    await breakConnection();
                }

                await _transaction.CommitAsync(token);
                return true;
            },
            command: null,
            FailureSite.Commit,
            cancellationToken);
        Volatile.Write(ref _state, (int)UnitOfWorkState.Committed);
    }

    /// <summary>
    /// Takes a transaction-scoped advisory lock keyed by the unit's own scope, the module, and the name (FR-027, research
    /// R14). <c>TryOnly</c> returns <c>false</c> when it is held and the unit stays open. A bounded wait runs under a
    /// transaction-local <c>lock_timeout</c>, restored afterwards; when it expires, PostgreSQL aborts the transaction, so
    /// the unit faults with <c>persistence.lock_timeout</c> (event 4006). PostgreSQL releases the lock at commit or rollback.
    /// </summary>
    public async ValueTask<bool> AcquireAdvisoryLockAsync(AdvisoryLockName name, AdvisoryLockWait wait, CancellationToken cancellationToken)
    {
        if (name is null || wait is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var key = AdvisoryLockKey.Derive(Scope.Owner, name.Module, name.Name);
        if (wait.Bound is not { } bound)
        {
            return await FrameworkCommand("SELECT pg_try_advisory_xact_lock($1)").With(key).ScalarAsync<bool>(cancellationToken);
        }

        // The OFFSET 0 fence keeps the subquery from being flattened, so the previous value is read before set_config runs.
        var milliseconds = Math.Max(1, (long)Math.Ceiling(bound.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var previous = await FrameworkCommand("SELECT s.previous, set_config('lock_timeout', $1, true) FROM (SELECT current_setting('lock_timeout') AS previous OFFSET 0) s")
            .With(milliseconds).ScalarAsync<string>(cancellationToken);
        var started = _timeProvider.GetTimestamp();
        try
        {
            await RunAsync(async (command, token) => await command.ExecuteScalarAsync(token), FrameworkCommand("SELECT pg_advisory_xact_lock($1)").With(key), FailureSite.AdvisoryLockWait, cancellationToken);
        }
        catch (GatewayException failure) when (failure.Code == ErrorCodes.PersistenceLockTimeout)
        {
            PersistenceLog.LogLockTimedOut(_logger, Module, Operation, _timeProvider.GetElapsedTime(started));
            throw;
        }

        await FrameworkCommand("SELECT set_config('lock_timeout', $1, true)").With(previous).ExecuteAsync(cancellationToken);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        var state = (UnitOfWorkState)Interlocked.Exchange(ref _state, (int)UnitOfWorkState.Closed);
        if (state == UnitOfWorkState.Closed)
        {
            return;
        }

        if (state == UnitOfWorkState.Open)
        {
            PersistenceLog.LogRolledBack(_logger, Module, Operation, "disposed");
        }

        _bounds.CancelAll();
        if (state != UnitOfWorkState.Committed)
        {
            await RollBackQuietlyAsync();
        }

        await _connection.DisposeAsync();
        await _transaction.DisposeAsync();
        _bounds.Dispose();
        PersistenceTelemetry.ConnectionReleased(Module);

        var outcome = state switch
        {
            UnitOfWorkState.Committed => "committed",
            UnitOfWorkState.Faulted => "faulted",
            _ => "rolled_back",
        };
        PersistenceTelemetry.RecordUnit(Module, Operation, _timeProvider.GetElapsedTime(_started), outcome);
        PersistenceTelemetry.End(_activity, outcome, Failure?.FailureClass);
    }

    /// <summary>
    /// A statement the framework itself runs, such as a test's catalog read. Module repository code uses
    /// <c>Command(TableAccess, sql)</c> instead, which checks the scope before the statement exists (FR-007).
    /// </summary>
    internal UnitCommand FrameworkCommand(string sql) => new(this, sql);

    /// <summary>
    /// A module statement on <paramref name="access"/>. An operator-scope unit given a tenant table, or a tenant-scope unit
    /// given an operator table, fails with <c>persistence.scope_not_permitted</c> and faults before the command exists (FR-007).
    /// </summary>
    internal UnitCommand Command(TableAccess access, string sql)
    {
        if (access is null || sql is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (ScopePreCheck && (access is TenantTable) == (Scope.Owner is OperatorScope))
        {
            throw Fault(FailureClassifier.Raised(ErrorCodes.PersistenceScopeNotPermitted, PersistenceFailureClass.ScopeViolation));
        }

        return new UnitCommand(this, sql);
    }

    /// <summary>
    /// Runs one operation on the connection: refuses a unit that is not open or already busy, bounds the operation by the
    /// statement bound and the caller's token, and turns any failure into the unit's recorded failure and exception.
    /// </summary>
    internal async ValueTask<T> RunAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> work, UnitCommand? command, FailureSite site, CancellationToken cancellationToken)
    {
        if (State != UnitOfWorkState.Open)
        {
            throw new GatewayException(ErrorCodes.Internal);
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw Fault(FailureClassifier.Raised(ErrorCodes.Internal, PersistenceFailureClass.Unexpected));
        }

        try
        {
            using var statement = _bounds.ForStatement(cancellationToken);
            try
            {
                if (command is null)
                {
                    return await work(null!, statement.Token);
                }

                await using var built = command.Build(_connection, _transaction, site);
                Interlocked.Increment(ref _statements);
                return await work(built, statement.Token);
            }
            catch (Exception exception) when (exception is not GatewayException)
            {
                throw Fault(FailureClassifier.Classify(exception, site, _bounds.Fired(statement, cancellationToken)));
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>Records the failure, faults the unit, logs it, and returns the exception to throw.</summary>
    internal GatewayException Fault(Classification classification)
    {
        var failure = classification.Failure;
        Interlocked.CompareExchange(ref _failure, failure, null);
        if (Interlocked.CompareExchange(ref _state, (int)UnitOfWorkState.Faulted, (int)UnitOfWorkState.Open) == (int)UnitOfWorkState.Open)
        {
            PersistenceLog.LogFaulted(_logger, Module, Operation, failure.FailureClass);
            if (failure.CommitOutcomeUnknown)
            {
                PersistenceLog.LogCommitOutcomeUnknown(_logger, Module, Operation);
            }

            if (failure.FailureClass is PersistenceFailureClass.TransientConflict or PersistenceFailureClass.ConcurrencyConflict or PersistenceFailureClass.UniqueConflict)
            {
                PersistenceTelemetry.RecordConflict(Module, Operation, failure.FailureClass);
            }

            if (failure.FailureClass == PersistenceFailureClass.Unexpected)
            {
                PersistenceLog.LogUnexpectedFailure(_logger, Module, Operation, classification.SqlStateClass);
            }
        }

        return classification.ToException();
    }

    private async Task RollBackQuietlyAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open || _transaction.IsCompleted())
        {
            return;
        }

        using var bound = new CancellationTokenSource(Settings.StatementTimeout, _timeProvider);
        try
        {
            await _transaction.RollbackAsync(bound.Token);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            // The connection is broken or busy. Disposing it ends the transaction on the server, which rolls it back.
        }
    }
}

/// <summary>Npgsql marks a finished transaction by clearing its connection. Owned by ISK-0004.</summary>
internal static class NpgsqlTransactionState
{
    public static bool IsCompleted(this NpgsqlTransaction transaction) => transaction.Connection is null;
}

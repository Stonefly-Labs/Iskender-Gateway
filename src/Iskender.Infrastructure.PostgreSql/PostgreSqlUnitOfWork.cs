using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
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

    /// <summary>The adapter type behind a port, for repository code. Any other implementation fails with <c>core.internal</c>.</summary>
    internal static PostgreSqlUnitOfWork From(IUnitOfWork unit) => unit as PostgreSqlUnitOfWork ?? throw new GatewayException(ErrorCodes.Internal);

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async (_, token) =>
            {
                await _transaction.CommitAsync(token);
                return true;
            },
            command: null,
            FailureSite.Commit,
            cancellationToken);
        Volatile.Write(ref _state, (int)UnitOfWorkState.Committed);
    }

    public ValueTask<bool> AcquireAdvisoryLockAsync(AdvisoryLockName name, AdvisoryLockWait wait, CancellationToken cancellationToken) =>
        throw Fault(FailureClassifier.Raised(ErrorCodes.Internal, PersistenceFailureClass.Unexpected));

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

/// <summary>Npgsql marks a finished transaction by clearing its connection.</summary>
internal static class NpgsqlTransactionState
{
    public static bool IsCompleted(this NpgsqlTransaction transaction) => transaction.Connection is null;
}

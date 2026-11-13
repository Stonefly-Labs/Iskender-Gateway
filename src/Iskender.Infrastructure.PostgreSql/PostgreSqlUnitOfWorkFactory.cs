using System.Data;
using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// Opens PostgreSQL units of work (FR-004, FR-005, FR-009, research R4, R5, R7). It checks the scope before any I/O,
/// refuses a passed deadline, acquires a connection under the earlier of <c>connectionTimeout</c> and the deadline,
/// and in one round trip begins the transaction and sets the transaction-local scope settings and
/// <c>statement_timeout</c>. Owned by ISK-0004.
/// </summary>
public sealed class PostgreSqlUnitOfWorkFactory : IUnitOfWorkFactory
{
    private const string OpenSql =
        "SELECT set_config('iskender.scope_kind', $1, true), set_config('iskender.organization_id', $2, true), " +
        "set_config('iskender.project_id', $3, true), set_config('statement_timeout', $4, true)";

    private readonly PostgreSqlDataSources _sources;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public PostgreSqlUnitOfWorkFactory(PostgreSqlDataSources sources, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        _sources = sources ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken) =>
        OpenAsync(scope, options, detached: false, cancellationToken);

    /// <summary>
    /// Opens a unit in <paramref name="scope"/>'s owner scope that ignores the scope's deadline and request token, bounded
    /// by <paramref name="options"/> alone, for cleanup after the request stopped, such as ISK-0005 undoing a lease
    /// acquire whose commit outcome is unknown. It never widens the owner scope.
    /// </summary>
    internal ValueTask<IUnitOfWork> OpenDetachedAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken) =>
        OpenAsync(scope, options, detached: true, cancellationToken);

    private async ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, bool detached, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw new GatewayException(ErrorCodes.PersistenceScopeRequired);
        }

        if (options is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var settings = _sources.Settings;
        var started = _timeProvider.GetTimestamp();
        var deadline = detached ? options.Deadline : Earlier(scope.Deadline, options.Deadline);
        var remaining = deadline is { } at ? at - _timeProvider.GetUtcNow() : (TimeSpan?)null;
        if (remaining <= TimeSpan.Zero)
        {
            PersistenceLog.LogConnectionFailed(_logger, options.Module, options.Operation, PersistenceFailureClass.Timeout);
            throw new GatewayException(ErrorCodes.PersistenceTimeout);
        }

        var bounds = new UnitBounds(
            Shorter(options.Timeout, remaining),
            Shorter(settings.StatementTimeout, remaining),
            Shorter(settings.ConnectionTimeout, remaining),
            _timeProvider,
            detached ? CancellationToken.None : scope.RequestCancellation,
            cancellationToken);

        var activity = PersistenceTelemetry.StartUnit(options.Module, options.Operation);
        NpgsqlConnection? connection = null;
        try
        {
            using (var acquire = bounds.Acquire())
            {
                try
                {
                    connection = await _sources.Runtime.OpenConnectionAsync(acquire.Token);
                }
                finally
                {
                    PersistenceTelemetry.RecordAcquire(options.Module, options.Operation, _timeProvider.GetElapsedTime(started), connection is null ? "failed" : "acquired");
                }

                var transaction = await connection.BeginTransactionAsync(Isolation(options.Isolation), acquire.Token);
                var (kind, organization, project) = Settings(scope.Owner);
                await using (var command = new NpgsqlCommand(OpenSql, connection, transaction))
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = kind });
                    command.Parameters.Add(new NpgsqlParameter { Value = organization });
                    command.Parameters.Add(new NpgsqlParameter { Value = project });
                    command.Parameters.Add(new NpgsqlParameter { Value = ((long)Math.Ceiling(bounds.Statement.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) });
                    await command.ExecuteNonQueryAsync(acquire.Token);
                }

                PersistenceTelemetry.ConnectionHeld(options.Module);
                return new PostgreSqlUnitOfWork(scope, options, connection, transaction, bounds, activity, started, settings, _timeProvider, _logger);
            }
        }
        catch (Exception exception) when (exception is not GatewayException)
        {
            var classification = FailureClassifier.Classify(exception, FailureSite.Statement, bounds.Fired(caller: cancellationToken));
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            bounds.Dispose();
            PersistenceLog.LogConnectionFailed(_logger, options.Module, options.Operation, classification.Failure.FailureClass);
            if (classification.Failure.FailureClass == PersistenceFailureClass.Unexpected)
            {
                PersistenceLog.LogUnexpectedFailure(_logger, options.Module, options.Operation, classification.SqlStateClass);
            }

            PersistenceTelemetry.End(activity, "failed", classification.Failure.FailureClass);
            throw classification.ToException();
        }
    }

    /// <summary>The transaction-local settings for each scope kind (data model, Transaction-local settings).</summary>
    internal static (string Kind, string Organization, string Project) Settings(OwnerScope owner) => owner switch
    {
        OrganizationScope organization => ("organization", organization.Organization.Value, ""),
        ProjectScope project => ("project", project.Organization.Value, project.Project.Value),
        OperatorScope => ("operator", "", ""),
        _ => throw new GatewayException(ErrorCodes.PersistenceScopeRequired),
    };

    private static IsolationLevel Isolation(UnitOfWorkIsolation isolation) => isolation switch
    {
        UnitOfWorkIsolation.RepeatableRead => IsolationLevel.RepeatableRead,
        UnitOfWorkIsolation.Serializable => IsolationLevel.Serializable,
        _ => IsolationLevel.ReadCommitted,
    };

    private static DateTimeOffset? Earlier(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;

    private static TimeSpan Shorter(TimeSpan bound, TimeSpan? remaining) => remaining is { } left && left < bound ? left : bound;
}

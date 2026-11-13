using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// Opens one ISK-0004 unit of work per coordination operation (ISK-0005 research R8): module <c>coordination</c>, Read
/// Committed, bounded by <c>operationTimeout</c> and the coordination scope's deadline, in the scope's own unit scope. A
/// persistence failure becomes the coordination code of contract Failures rule 2, keeping its failure class and, for a
/// unit that writes, marking <c>outcome = unknown</c> when the commit's outcome is unknown. A unit that only reads changes
/// nothing, so its outcome is always known. With a <see cref="CoordinationReport"/>, a mapped
/// failure writes the contract's Diagnostics events: 5003 for a failed connection attempt or a refused credential, with
/// its class, 5004 when the outcome is unknown, and 5010 for an unexpected failure. A commit cut off with its outcome
/// unknown, such as by a token or bound firing during <c>COMMIT</c>, is not a connection attempt, so it writes no 5003.
/// The caller waits no longer than the bound or its tokens: the driver's cancel request to a server that stopped answering
/// can take <c>connectionTimeout</c> and more, so the unit is abandoned at the bound and finishes in the background, and
/// once abandoned it never commits. Owned by ISK-0005.
/// </summary>
internal static class CoordinationUnits
{
    /// <summary>
    /// The unit options for <paramref name="operation"/> in <paramref name="scope"/>; a <paramref name="detached"/> unit is
    /// bounded by <paramref name="operationTimeout"/> alone.
    /// </summary>
    public static UnitOfWorkOptions Options(CoordinationScope scope, string operation, TimeSpan operationTimeout, bool detached = false) =>
        new(CoordinationMigrations.Name, operation)
        {
            Isolation = UnitOfWorkIsolation.ReadCommitted,
            Timeout = operationTimeout,
            Deadline = detached ? null : scope?.Deadline,
        };

    /// <summary>
    /// Runs <paramref name="work"/> in its own unit and commits it. A coordination failure the work raises passes through
    /// unchanged; any other failure is mapped by <see cref="Map(GatewayException, PersistenceFailure?, bool)"/> and reported to
    /// <paramref name="report"/>. Only a unit that <paramref name="writes"/> can have an unknown outcome. A
    /// <paramref name="detached"/> unit, for cleanup after the request stopped, runs in the scope's owner scope but ignores
    /// its deadline and request token, bounded by <paramref name="operationTimeout"/> alone. When the bound or the caller's
    /// or request's token fires first, the call fails at once with <c>coordination.timeout</c> or
    /// <c>coordination.cancelled</c>, with <c>outcome = unknown</c> (and event 5004) only for a unit that
    /// <paramref name="writes"/> and had already sent <c>COMMIT</c>.
    /// </summary>
    public static async ValueTask<T> RunAsync<T>(
        IUnitOfWorkFactory units,
        CoordinationScope scope,
        string operation,
        TimeSpan operationTimeout,
        Func<IUnitOfWork, CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken,
        CoordinationReport? report = null,
        bool writes = true,
        bool detached = false)
    {
        if (units is null || scope is null || work is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var requestCancellation = detached ? CancellationToken.None : scope.RequestCancellation;
        var gate = new CommitGate();
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = RunUnitAsync(units, scope, operation, operationTimeout, work, report, writes, detached, gate, abandon.Token).AsTask();
        using var bound = new CancellationTokenSource(Bound(scope, operationTimeout, detached));
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCancellation, bound.Token);
        try
        {
            return await running.WaitAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
            if (running.IsCompleted)
            {
                // It finished as the bound fired, and reported its own outcome.
                return await running;
            }

            var commitSent = gate.Abandon();

            // The unit is left to finish on its own; its failure is observed there, and it can no longer commit.
            _ = running.ContinueWith(static finished => _ = finished.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await abandon.CancelAsync();
            var cancelled = cancellationToken.IsCancellationRequested || requestCancellation.IsCancellationRequested;
            var outcomeUnknown = writes && commitSent;
            if (outcomeUnknown && report is not null)
            {
                CoordinationLog.LogOutcomeUnknown(report.Logger, report.Module, report.Primitive, operation, CoordinatorKind.PostgreSql);
            }

            throw cancelled
                ? CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled, outcomeUnknown)
                : CoordinationFailures.Raise(ErrorCodes.CoordinationTimeout, CoordinationFailureClass.Timeout, outcomeUnknown);
        }
    }

    private static async ValueTask<T> RunUnitAsync<T>(
        IUnitOfWorkFactory units,
        CoordinationScope scope,
        string operation,
        TimeSpan operationTimeout,
        Func<IUnitOfWork, CancellationToken, ValueTask<T>> work,
        CoordinationReport? report,
        bool writes,
        bool detached,
        CommitGate gate,
        CancellationToken cancellationToken)
    {
        IUnitOfWork unit;
        try
        {
            var options = Options(scope, operation, operationTimeout, detached);
            unit = detached && units is PostgreSqlUnitOfWorkFactory factory
                ? await factory.OpenDetachedAsync(scope.UnitScope, options, cancellationToken)
                : await units.OpenAsync(scope.UnitScope, options, cancellationToken);
        }
        catch (GatewayException failure)
        {
            // No unit records an open failure, so its class is the one the factory classified it with.
            throw gate.Report(live => Report(Map(failure, Classification.RecordedFor(failure)), failure, live, operation), report);
        }

        await using (unit)
        {
            try
            {
                var result = await work(unit, cancellationToken);
                gate.Commit(cancellationToken);
                await unit.CommitAsync(cancellationToken);
                return result;
            }
            catch (GatewayException failure)
            {
                throw gate.Report(live => Report(Map(failure, unit.Failure, writes), failure, live, operation, unit.Failure), report);
            }
        }
    }

    // The earlier of operationTimeout and the scope's deadline, or operationTimeout alone for a detached unit; a passed
    // deadline gives a bound of one tick, so the unit's own deadline check reports it.
    private static TimeSpan Bound(CoordinationScope scope, TimeSpan operationTimeout, bool detached)
    {
        var bound = operationTimeout;
        if (!detached && scope.Deadline is { } deadline)
        {
            var remaining = deadline - TimeProvider.System.GetUtcNow();
            bound = remaining < bound ? remaining : bound;
        }

        return bound > TimeSpan.Zero ? bound : TimeSpan.FromTicks(1);
    }

    /// <summary>
    /// Whether a unit sent <c>COMMIT</c>, decided once against the caller abandoning it: a commit starts only while the
    /// call is live, and abandoning reports whether one had started. Only a live call reports its own failure.
    /// </summary>
    private sealed class CommitGate
    {
        private readonly Lock _lock = new();
        private bool _committing;
        private bool _abandoned;

        /// <summary>Lets the commit start, or throws <c>coordination.cancelled</c> when the call was abandoned.</summary>
        public void Commit(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_abandoned)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                _committing = true;
            }
        }

        /// <summary>Stops the commit if it has not started, and returns whether it had.</summary>
        public bool Abandon()
        {
            lock (_lock)
            {
                _abandoned = true;
                return _committing;
            }
        }

        /// <summary>
        /// Builds the failure with <paramref name="report"/>, writing its events to <paramref name="target"/> only while the
        /// call is live: an abandoned call was already answered and reported.
        /// </summary>
        public GatewayException Report(Func<CoordinationReport?, GatewayException> report, CoordinationReport? target)
        {
            lock (_lock)
            {
                return report(_abandoned ? null : target);
            }
        }
    }

    /// <summary>
    /// Writes events 5003, 5004, and 5010 for <paramref name="mapped"/> to <paramref name="report"/>, and returns it. A
    /// coordination failure that passed through unmapped was reported, if at all, where it was raised. When the unit's
    /// <paramref name="recorded"/> failure is a commit whose outcome is unknown, no 5003 is written.
    /// </summary>
    public static GatewayException Report(GatewayException mapped, GatewayException original, CoordinationReport? report, string operation, PersistenceFailure? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(mapped);
        if (report is null || ReferenceEquals(mapped, original))
        {
            return mapped;
        }

        var failureClass = CoordinationFailures.ClassOf(mapped);
        if (failureClass is CoordinationFailureClass.Unavailable or CoordinationFailureClass.AuthenticationFailed && recorded?.CommitOutcomeUnknown != true)
        {
            CoordinationLog.LogConnectionFailed(report.Logger, CoordinatorKind.PostgreSql, failureClass.Value);
        }

        if (failureClass == CoordinationFailureClass.Unexpected)
        {
            CoordinationLog.LogUnexpectedFailure(report.Logger, report.Module, operation, CoordinatorKind.PostgreSql, CoordinationFailureClass.Unexpected);
        }

        if (CoordinationFailures.IsOutcomeUnknown(mapped))
        {
            CoordinationLog.LogOutcomeUnknown(report.Logger, report.Module, report.Primitive, operation, CoordinatorKind.PostgreSql);
        }

        return mapped;
    }

    /// <summary>
    /// The coordination failure for a unit's <paramref name="failure"/>: <c>persistence.unavailable</c>,
    /// <c>persistence.timeout</c>, and <c>persistence.cancelled</c> become the matching coordination codes, keeping the
    /// failure class, and anything else <c>core.internal</c> with class <c>Unexpected</c>, as the contract Failures table
    /// pairs them. The outcome is unknown only for a unit that <paramref name="writes"/>.
    /// </summary>
    public static GatewayException Map(PersistenceFailure failure, bool writes = true)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var code = CodeFor(failure.Code);
        var failureClass = ReferenceEquals(code, ErrorCodes.Internal) ? CoordinationFailureClass.Unexpected : ClassFor(failure.FailureClass);
        return CoordinationFailures.Raise(code, failureClass, writes && failure.CommitOutcomeUnknown);
    }

    /// <summary>
    /// The coordination failure for <paramref name="exception"/>. A coordination code or <c>core.value.invalid</c> is
    /// already a contract failure and is returned as is; otherwise the unit's recorded <paramref name="failure"/> is used,
    /// or, when the unit never opened or recorded none, the class implied by the code.
    /// </summary>
    public static GatewayException Map(GatewayException exception, PersistenceFailure? failure, bool writes = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Code.Id.StartsWith("coordination.", StringComparison.Ordinal) || ReferenceEquals(exception.Code, ErrorCodes.ValueInvalid))
        {
            return exception;
        }

        if (failure is not null && ReferenceEquals(failure.Code, exception.Code))
        {
            return Map(failure, writes);
        }

        var code = CodeFor(exception.Code);
        var failureClass = code == ErrorCodes.CoordinationUnavailable ? CoordinationFailureClass.Unavailable
            : code == ErrorCodes.CoordinationTimeout ? CoordinationFailureClass.Timeout
            : code == ErrorCodes.CoordinationCancelled ? CoordinationFailureClass.Cancelled
            : CoordinationFailureClass.Unexpected;
        return CoordinationFailures.Raise(code, failureClass);
    }

    private static ErrorCode CodeFor(ErrorCode code) =>
        ReferenceEquals(code, ErrorCodes.PersistenceUnavailable) ? ErrorCodes.CoordinationUnavailable
        : ReferenceEquals(code, ErrorCodes.PersistenceTimeout) ? ErrorCodes.CoordinationTimeout
        : ReferenceEquals(code, ErrorCodes.PersistenceCancelled) ? ErrorCodes.CoordinationCancelled
        : ErrorCodes.Internal;

    private static CoordinationFailureClass ClassFor(PersistenceFailureClass failureClass) => failureClass switch
    {
        PersistenceFailureClass.Unavailable => CoordinationFailureClass.Unavailable,
        PersistenceFailureClass.Timeout => CoordinationFailureClass.Timeout,
        PersistenceFailureClass.Cancelled => CoordinationFailureClass.Cancelled,
        PersistenceFailureClass.AuthenticationFailed => CoordinationFailureClass.AuthenticationFailed,
        _ => CoordinationFailureClass.Unexpected,
    };
}

/// <summary>Where a PostgreSQL coordinator reports a failure, and the module and primitive it is labelled with.</summary>
internal sealed record CoordinationReport(ILogger Logger, string Module, CoordinationPrimitive Primitive);

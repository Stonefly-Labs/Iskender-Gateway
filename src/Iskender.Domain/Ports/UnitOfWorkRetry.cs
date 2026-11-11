using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Ports;

/// <summary>
/// Runs a whole unit of work again after a transient conflict (FR-014, research R9). Each attempt opens a new unit, runs
/// the work, and commits. When the work or the commit throws, the helper reads the unit's recorded
/// <see cref="IUnitOfWork.Failure"/> before disposing it, and runs again only when the class is
/// <see cref="PersistenceFailureClass.TransientConflict"/>, the commit outcome is known, and
/// <see cref="UnitOfWorkOptions.SafeToRerun"/> is true. An exception with no recorded failure is never retried. It makes at
/// most 3 attempts in all, then fails with <c>persistence.concurrency_conflict</c>. Between attempts it waits a jitter of at
/// most 50 ms, drawn from the injected <see cref="TimeProvider"/>'s clock, and a cancellation during that wait fails with
/// <c>persistence.cancelled</c>. It logs event 4005 and counts
/// <c>iskender.persistence.retries</c> under the meter <c>Iskender.Persistence</c>. Owned by ISK-0004.
/// </summary>
public static class UnitOfWorkRetry
{
    /// <summary>Attempts in all, the first included.</summary>
    public const int MaxAttempts = 3;

    /// <summary>The longest wait between attempts.</summary>
    public static readonly TimeSpan MaxJitter = TimeSpan.FromMilliseconds(50);

    private const string TransientConflict = "transient_conflict";

    private static readonly Meter Meter = new("Iskender.Persistence");

    private static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        "iskender.persistence.retries", "{retry}", "Units of work run again after a transient conflict.");

    private static readonly Action<ILogger, string, string, string, Exception?> Retried =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information, new EventId(4005, "TransientConflictRetried"),
            "Transient conflict retried: module {Module}, operation {Operation}, class {FailureClass}.");

    /// <summary>
    /// Opens a unit, runs <paramref name="work"/>, commits, and returns its result, running the whole unit again after a
    /// transient conflict when <see cref="UnitOfWorkOptions.SafeToRerun"/> allows it.
    /// </summary>
    public static async ValueTask<T> RunAsync<T>(
        IUnitOfWorkFactory factory,
        UnitOfWorkScope scope,
        UnitOfWorkOptions options,
        Func<IUnitOfWork, CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        if (factory is null || options is null || work is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        timeProvider ??= TimeProvider.System;
        logger ??= NullLogger.Instance;
        for (var attempt = 1; ; attempt++)
        {
            var unit = await factory.OpenAsync(scope, options, cancellationToken);
            try
            {
                var result = await work(unit, cancellationToken);
                await unit.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception) when (Retryable(unit.Failure, options))
            {
                if (attempt == MaxAttempts)
                {
                    throw new GatewayException(ErrorCodes.PersistenceConcurrencyConflict);
                }

                Retried(logger, options.Module, options.Operation, TransientConflict, null);
                Retries.Add(1, new("iskender.module", options.Module), new("iskender.operation", options.Operation), new("iskender.failure_class", TransientConflict));
            }
            finally
            {
                await unit.DisposeAsync();
            }

            var jitter = TimeSpan.FromMilliseconds(timeProvider.GetTimestamp() % (long)(MaxJitter.TotalMilliseconds + 1));
            if (jitter > TimeSpan.Zero)
            {
                // A cancellation during the wait fails with a registered code, as every port does (contract Failures rule 1).
                try
                {
                    await Task.Delay(jitter, timeProvider, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new GatewayException(ErrorCodes.PersistenceCancelled);
                }
            }
        }
    }

    private static bool Retryable(PersistenceFailure? failure, UnitOfWorkOptions options) =>
        options.SafeToRerun && failure is { FailureClass: PersistenceFailureClass.TransientConflict, CommitOutcomeUnknown: false };
}

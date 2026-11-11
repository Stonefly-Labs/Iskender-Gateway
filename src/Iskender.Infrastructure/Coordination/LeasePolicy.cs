using System.Security.Cryptography;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// What every lease coordinator does the same way (research R12): the duration and wait bounds checked before any I/O,
/// a passed deadline failing before any I/O, and a bounded wait that polls with jittered backoff on a
/// <see cref="TimeProvider"/>, ending at the bound with contention, at the deadline with <c>coordination.timeout</c>, or on
/// cancellation with <c>coordination.cancelled</c>. Public because the Redis and PostgreSQL coordinators use it too.
/// Owned by ISK-0005.
/// </summary>
public static class LeasePolicy
{
    /// <summary>The first poll interval of a bounded wait.</summary>
    public static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(10);

    /// <summary>The longest poll interval of a bounded wait.</summary>
    public static readonly TimeSpan LongestPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>A <c>null</c> scope fails with <c>coordination.scope_required</c>, and a <c>null</c> name with <c>core.value.invalid</c>.</summary>
    public static void RequireTarget(CoordinationScope scope, CoordinationName name)
    {
        if (scope is null)
        {
            throw new GatewayException(ErrorCodes.CoordinationScopeRequired);
        }

        if (name is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>A duration outside <c>[leases.minimumDuration, leases.maximumDuration]</c> fails with <c>core.value.invalid</c>.</summary>
    public static void RequireDuration(CoordinationSettings settings, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (duration < settings.MinimumLeaseDuration || duration > settings.MaximumLeaseDuration)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>A <c>null</c> wait, or a bound above <c>leases.maximumWait</c>, fails with <c>core.value.invalid</c>.</summary>
    public static void RequireWait(CoordinationSettings settings, LeaseWait wait)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (wait is null || wait.Bound > settings.MaximumWait)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>
    /// Fails with <c>coordination.cancelled</c> when <paramref name="cancellationToken"/> or the scope's request token is
    /// cancelled, and with <c>coordination.timeout</c> when the scope's deadline has passed on <paramref name="timeProvider"/>.
    /// </summary>
    public static void RequireTime(CoordinationScope scope, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (cancellationToken.IsCancellationRequested || scope.RequestCancellation.IsCancellationRequested)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled);
        }

        if (scope.Deadline is { } deadline && deadline <= timeProvider.GetUtcNow())
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationTimeout, CoordinationFailureClass.Timeout);
        }
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> once for <see cref="LeaseWait.TryOnly"/>, or until it acquires or the bound passes,
    /// sleeping between attempts for a jittered, doubling interval from <see cref="FirstPoll"/> to <see cref="LongestPoll"/>,
    /// never past the bound or the deadline. Gives no fairness or ordering between waiters.
    /// </summary>
    public static async ValueTask<LeaseAcquisition> AcquireAsync(
        CoordinationScope scope,
        LeaseWait wait,
        TimeProvider timeProvider,
        Func<CancellationToken, ValueTask<LeaseAcquisition>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(attempt);
        RequireTime(scope, timeProvider, cancellationToken);
        var started = timeProvider.GetTimestamp();
        var interval = FirstPoll;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scope.RequestCancellation);
        while (true)
        {
            var acquisition = await attempt(linked.Token);
            if (acquisition.IsAcquired || wait.Bound is not { } bound)
            {
                return acquisition;
            }

            var left = bound - timeProvider.GetElapsedTime(started);
            if (left <= TimeSpan.Zero)
            {
                return acquisition;
            }

            var sleep = Jitter(interval);
            sleep = sleep < left ? sleep : left;
            if (scope.Deadline is { } deadline && deadline - timeProvider.GetUtcNow() is var untilDeadline && untilDeadline < sleep)
            {
                // The deadline comes before the next attempt would: sleep until it, then fail with a timeout.
                sleep = untilDeadline > TimeSpan.Zero ? untilDeadline : TimeSpan.Zero;
            }

            try
            {
                await Task.Delay(sleep, timeProvider, linked.Token);
            }
            catch (OperationCanceledException)
            {
                throw CoordinationFailures.Raise(ErrorCodes.CoordinationCancelled, CoordinationFailureClass.Cancelled);
            }

            RequireTime(scope, timeProvider, cancellationToken);
            interval = interval * 2 < LongestPoll ? interval * 2 : LongestPoll;
        }
    }

    // Between half and all of the interval, so that waiters polling one key spread out.
    private static TimeSpan Jitter(TimeSpan interval) =>
        TimeSpan.FromTicks((interval.Ticks / 2) + RandomNumberGenerator.GetInt32((int)Math.Min(int.MaxValue, (interval.Ticks / 2) + 1)));
}

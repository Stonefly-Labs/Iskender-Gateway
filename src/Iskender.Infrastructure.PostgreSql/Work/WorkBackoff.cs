namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The retry delay of <c>work</c> contract Retry and poison rule 2: for attempt <c>n</c>,
/// <c>d(n) = min(maximumDelay, initialDelay × 2^(n−1))</c>, and the delay is drawn uniformly from
/// <c>[max(initialDelay, d(n) ÷ 2), d(n)]</c>. Pure, with the random source injected. Owned by ISK-0009.
/// </summary>
internal static class WorkBackoff
{
    /// <summary>The delay before attempt <paramref name="attempt"/> + 1, after attempt <paramref name="attempt"/> failed.</summary>
    /// <param name="attempt">The failed attempt, from 1.</param>
    /// <param name="initialDelay">The work section's <c>retry.initialDelay</c>.</param>
    /// <param name="maximumDelay">The work section's <c>retry.maximumDelay</c>, at least <paramref name="initialDelay"/>.</param>
    /// <param name="random">A uniform draw in <c>[0, 1)</c>.</param>
    public static TimeSpan Delay(int attempt, TimeSpan initialDelay, TimeSpan maximumDelay, Func<double> random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDelay, initialDelay);
        ArgumentNullException.ThrowIfNull(random);

        var ceiling = Ceiling(attempt, initialDelay.Ticks, maximumDelay.Ticks);
        var floor = Math.Max(initialDelay.Ticks, ceiling / 2);
        var draw = Math.Clamp(random(), 0d, 1d);
        return TimeSpan.FromTicks(floor + (long)Math.Floor((ceiling - floor) * draw));
    }

    // d(n) in ticks, without overflow: initialDelay doubled n − 1 times, stopping at maximumDelay.
    private static long Ceiling(int attempt, long initial, long maximum)
    {
        var ceiling = initial;
        for (var doubling = 1; doubling < attempt && ceiling < maximum; doubling++)
        {
            ceiling = ceiling > maximum / 2 ? maximum : ceiling * 2;
        }

        return Math.Min(ceiling, maximum);
    }
}

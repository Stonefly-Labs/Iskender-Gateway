using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Retry;

/// <summary>The retry delay of <c>work</c> contract Retry and poison rule 2 (ISK-0009 T016, FR-016, R7).</summary>
public class WorkBackoffTests
{
    private static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Maximum = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(0d)]
    [InlineData(0.5d)]
    [InlineData(0.999999d)]
    public void AttemptOneIsExactlyTheInitialDelay(double draw)
    {
        Assert.Equal(Initial, WorkBackoff.Delay(1, Initial, Maximum, () => draw));
    }

    [Theory]
    [InlineData(2, 1_000, 2_000)]
    [InlineData(3, 2_000, 4_000)]
    [InlineData(4, 4_000, 8_000)]
    [InlineData(9, 128_000, 256_000)]
    [InlineData(10, 150_000, 300_000)]
    public void AttemptNIsDrawnBetweenHalfItsCeilingAndItsCeiling(int attempt, int lowestMilliseconds, int highestMilliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(lowestMilliseconds), WorkBackoff.Delay(attempt, Initial, Maximum, () => 0d));
        Assert.Equal(TimeSpan.FromMilliseconds(highestMilliseconds), WorkBackoff.Delay(attempt, Initial, Maximum, () => 1d));
        var middle = WorkBackoff.Delay(attempt, Initial, Maximum, () => 0.5d);
        Assert.InRange(middle, TimeSpan.FromMilliseconds(lowestMilliseconds), TimeSpan.FromMilliseconds(highestMilliseconds));
    }

    [Fact]
    public void NoDrawExceedsTheMaximumDelayAndNoAttemptOverflows()
    {
        var random = new Random(20261117);
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            for (var sample = 0; sample < 50; sample++)
            {
                var delay = WorkBackoff.Delay(attempt, Initial, Maximum, random.NextDouble);
                Assert.InRange(delay, Initial, Maximum);
            }
        }

        Assert.Equal(TimeSpan.FromHours(24), WorkBackoff.Delay(100, TimeSpan.FromHours(1), TimeSpan.FromHours(24), () => 1d));
        Assert.Equal(TimeSpan.FromHours(12), WorkBackoff.Delay(100, TimeSpan.FromHours(1), TimeSpan.FromHours(24), () => 0d));
    }

    [Fact]
    public void AnInitialDelayEqualToTheMaximumIsAlwaysThatDelay()
    {
        Assert.Equal(Initial, WorkBackoff.Delay(7, Initial, Initial, () => 0d));
        Assert.Equal(Initial, WorkBackoff.Delay(7, Initial, Initial, () => 1d));
    }
}

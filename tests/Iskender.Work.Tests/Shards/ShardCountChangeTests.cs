using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Shards;

/// <summary>
/// A change of shard count (ISK-0009 T029, US2.4, FR-013): dispatchers with <c>dispatch.shards</c> 2 and 4 hold
/// different lease keys over one scope's items, and the conditional writes still let one holder apply each item.
/// </summary>
public class ShardCountChangeTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly int[] ShardCounts = [2, 4, 2, 4];

    [Fact]
    public async Task DispatchersWithTwoAndFourShardsApplyEachItemOnce()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var items = new List<Guid>();
        for (var index = 0; index < 40; index++)
        {
            items.Add(await work.EnqueueAsync(scope, SamplingHandler.Kind, "count-" + index));
        }

        var dispatchers = ShardCounts
            .Select(shards => new TestDispatcher(work, [SamplingHandler.Registration()], WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":{shards}}}"))
            .ToList();
        try
        {
            dispatchers.ForEach(dispatcher => dispatcher.Start());
            foreach (var item in items)
            {
                await Eventually.StateAsync(work, item, "completed");
            }
        }
        finally
        {
            foreach (var dispatcher in dispatchers)
            {
                await dispatcher.DisposeAsync();
            }
        }

        foreach (var item in items)
        {
            Assert.Equal(1L, await work.AppliesAsync(item));
        }

        Assert.True(dispatchers.Count(dispatcher => dispatcher.Handler<SamplingHandler>().Handled > 0) > 1, "One dispatcher handled every item.");
    }
}

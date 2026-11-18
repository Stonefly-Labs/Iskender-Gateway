using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Shards;

/// <summary>
/// Shard spread (ISK-0009 T028, US2.3, SC-002, FR-011): one scope's items in every shard and as many dispatchers as
/// shards spread over more than one dispatcher, within each one's in-flight caps, and a dead dispatcher's shard stalls alone.
/// </summary>
public class ShardSpreadTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const int Shards = 4;

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public async Task OneScopesShardsSpreadOverTheDispatchersWithinTheirCaps(int concurrency, int scopeConcurrency)
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var items = await EnqueueInEveryShardAsync(scope, SamplingHandler.Kind, perShard: 6);
        var fields = WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":{Shards},\"concurrency\":{concurrency},\"scopeConcurrency\":{scopeConcurrency}}}";

        var dispatchers = Enumerable.Range(0, Shards).Select(_ => new TestDispatcher(work, [SamplingHandler.Registration()], fields)).ToList();
        try
        {
            dispatchers.ForEach(dispatcher => dispatcher.Start());
            foreach (var item in items.SelectMany(shard => shard))
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

        var handlers = dispatchers.Select(dispatcher => dispatcher.Handler<SamplingHandler>()).ToList();
        Assert.True(handlers.Count(handler => handler.Handled > 0) > 1, "One dispatcher handled every item.");
        Assert.All(handlers, handler => Assert.InRange(handler.Maximum, 0, Math.Min(concurrency, scopeConcurrency)));
        Assert.Equal(items.Sum(shard => shard.Count), handlers.Sum(handler => handler.Handled));
        foreach (var item in items.SelectMany(shard => shard))
        {
            Assert.Equal(1L, await work.AppliesAsync(item));
        }
    }

    [Fact]
    public async Task ADeadDispatchersShardStallsWhileTheOtherShardsComplete()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var items = await EnqueueInEveryShardAsync(scope, TestKinds.Record, perShard: 3);

        // The default 30 s lease keeps the dead dispatcher's shard held well past the others' completion.
        var fields = $"\"pollInterval\":\"1s\",\"dispatch\":{{\"shards\":{Shards},\"scopeConcurrency\":1}}";
        var dead = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], fields);
        var held = dead.Injection.At(WorkInjectionPoint.DuringHandler);
        var pass = dead.StartPass();
        var stalledItem = await held.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        await dead.AbandonAsync();
        await pass.ContinueWith(_ => { }, TaskScheduler.Default);
        await dead.DisposeAsync();
        var stalled = ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(stalledItem), Shards);

        var others = Enumerable.Range(0, Shards - 1).Select(_ => new TestDispatcher(work, [ApplyRecordingHandler.Registration()], fields)).ToList();
        try
        {
            others.ForEach(dispatcher => dispatcher.Start());
            foreach (var item in items.Where((_, shard) => shard != stalled).SelectMany(shard => shard))
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            foreach (var item in items[stalled])
            {
                Assert.StartsWith("pending", await work.ItemAsync(item), StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var dispatcher in others)
            {
                await dispatcher.DisposeAsync();
            }
        }
    }

    // Enqueues items in scope until each shard has perShard of them, returning them by shard.
    private async Task<List<List<Guid>>> EnqueueInEveryShardAsync(UnitOfWorkScope scope, WorkKind kind, int perShard)
    {
        var byShard = Enumerable.Range(0, Shards).Select(_ => new List<Guid>()).ToList();
        var extra = new List<Guid>();
        for (var index = 0; byShard.Any(shard => shard.Count < perShard); index++)
        {
            var item = await work.EnqueueAsync(scope, kind, "spread-" + index);
            var shard = byShard[ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), Shards)];
            (shard.Count < perShard ? shard : extra).Add(item);
        }

        // Items past a shard's quota are still the scope's, so they join their shard's list.
        foreach (var item in extra)
        {
            byShard[ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), Shards)].Add(item);
        }

        return byShard;
    }
}

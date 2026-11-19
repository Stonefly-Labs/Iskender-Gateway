using System.Diagnostics;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Shards;

/// <summary>
/// Shard spread (ISK-0009 T028, US2.3, SC-002, FR-011): one scope's items in every shard and as many dispatchers as
/// shards spread over more than one dispatcher, within each one's in-flight caps, and a dead dispatcher's shard stalls alone.
/// Across scopes, a worker's cap binds and a hung handler in one scope leaves the others progressing (T067), items
/// enqueued while handlers hang still complete (T084), and a shard's backlog leaves its scope's other shards a slot (T086).
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

    // A directory page's scopes run side by side, so one worker runs more than one scope's cap at once, and never more than
    // its own (ISK-0009 T067, FR-011, SC-002).
    [Fact]
    public async Task TheWorkerCapBindsAcrossSeveralScopes()
    {
        work.RequirePostgreSql();
        const int concurrency = 3;
        const int scopeConcurrency = 2;
        var items = new List<Guid>();
        for (var scope = 0; scope < 4; scope++)
        {
            var owner = WorkFixture.FreshOrganization();
            for (var index = 0; index < 6; index++)
            {
                items.Add(await work.EnqueueAsync(owner, SamplingHandler.Kind, $"cap-{scope}-{index}"));
            }
        }

        var fields = WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":1,\"concurrency\":{concurrency},\"scopeConcurrency\":{scopeConcurrency}}}";
        await using (var dispatcher = new TestDispatcher(work, [SamplingHandler.Registration()], fields))
        {
            dispatcher.Start();
            foreach (var item in items)
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            Assert.InRange(dispatcher.Handler<SamplingHandler>().Maximum, scopeConcurrency + 1, concurrency);
        }

        foreach (var item in items)
        {
            Assert.Equal(1L, await work.AppliesAsync(item));
        }
    }

    // A hung handler holds only its own scope's slot: a scope listed after it on the same page completes meanwhile
    // (ISK-0009 T067, FR-018, TR-6).
    [Fact]
    public async Task AHungHandlerInOneScopeLeavesOtherScopesProgressing()
    {
        work.RequirePostgreSql();
        var first = WorkFixture.FreshOrganization();
        var second = WorkFixture.FreshOrganization();
        var firstItem = await work.EnqueueAsync(first, TestKinds.Record, "order-first");
        var secondItem = await work.EnqueueAsync(second, TestKinds.Record, "order-second");

        // The hung item goes into whichever scope the directory pages first, so a sequential pass would reach the other
        // scope only after the hung handler's timeout.
        var listedFirst = await work.ScalarAsync<Guid>(
            "SELECT i.item_id FROM isk_work.items i JOIN isk_directory.scopes d ON d.module = 'work' AND d.listed_scope = i.scope_key "
            + $"WHERE i.item_id IN ('{firstItem}', '{secondItem}') ORDER BY d.listed_scope LIMIT 1");
        var (hungScope, otherScope, others) = listedFirst == firstItem ? (first, second, new List<Guid> { secondItem }) : (second, first, [firstItem]);
        var hung = await work.EnqueueAsync(hungScope, HangHandler.Kind, "hung");
        for (var index = 0; index < 2; index++)
        {
            others.Add(await work.EnqueueAsync(otherScope, TestKinds.Record, "other-" + index));
        }

        var fields = WorkTestSettings.Fast + ",\"dispatch\":{\"shards\":1,\"concurrency\":4,\"scopeConcurrency\":1}";
        await using (var dispatcher = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(5)), ApplyRecordingHandler.Registration()], fields))
        {
            dispatcher.Start();
            foreach (var item in others)
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            Assert.False(HangHandler.Ran.ContainsKey(hung), "The hung handler ended before the other scope completed.");
        }

        await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{hung}' AND state = 'pending'");
    }

    // A pass claims without waiting for the shards already running, so while a handler hangs in one scope, and in one shard
    // of another, items enqueued after the hang started, in a third scope and in another shard of the second, complete
    // within a few poll intervals (ISK-0009 T084, FR-018, spec Edge Cases "a handler hangs"). Every item here is
    // enqueued after the first pass, unlike the tests above.
    [Fact]
    public async Task ItemsEnqueuedWhileHandlersHangCompleteWithinAFewPollIntervals()
    {
        work.RequirePostgreSql();
        const int shards = 2;
        var hungScope = WorkFixture.FreshOrganization();
        var sharedScope = WorkFixture.FreshOrganization();
        var hung = new List<Guid>
        {
            await work.EnqueueAsync(hungScope, HangHandler.Kind, "while-hung-a"),
            await work.EnqueueAsync(sharedScope, HangHandler.Kind, "while-hung-b"),
        };
        var hungShard = ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(hung[1]), shards);
        var fields = WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":{shards},\"concurrency\":4,\"scopeConcurrency\":2}}";
        var waiting = new Stopwatch();
        var later = new List<Guid>();
        await using (var dispatcher = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(5)), ApplyRecordingHandler.Registration()], fields))
        {
            dispatcher.Start();
            await Eventually.HoldsAsync(() => Task.FromResult(dispatcher.Inner.InFlight == 2), "both handlers hanging");

            later.Add(await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "after-other-scope"));
            for (var index = 0; ; index++)
            {
                var item = await work.EnqueueAsync(sharedScope, TestKinds.Record, "after-same-scope-" + index);
                if (ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), shards) != hungShard)
                {
                    later.Add(item);
                    break;
                }

                // An item in the hung shard waits for it; it is taken out of later tests below.
                hung.Add(item);
            }

            waiting.Start();
            foreach (var item in later)
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            waiting.Stop();
            Assert.All(hung.Take(2), item => Assert.False(HangHandler.Ran.ContainsKey(item), "A hung handler ended before the later items completed."));
        }

        Assert.InRange(waiting.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        foreach (var item in hung)
        {
            await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}' AND state = 'pending'");
        }
    }

    // A scope's shards are tried from where its last pass stopped, so a backlog in shard 0 does not keep the scope's one
    // slot from its other shards: their items complete before shard 0's backlog drains (ISK-0009 T086, Dispatch and apply
    // rules 1 and 3, FR-010, TR-6).
    [Fact]
    public async Task AShardBacklogDoesNotKeepTheScopesSlotFromItsOtherShards()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var backlog = new List<Guid>();
        var others = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            backlog.Add(await work.EnqueueAsync(scope, TestKinds.Record, "backlog-" + index));
        }

        for (var shard = 1; shard < Shards; shard++)
        {
            others.Add(await work.EnqueueAsync(scope, TestKinds.Record, "other-shard-" + shard));
        }

        // The shard is the bucket modulo the shard count, so the buckets place the items exactly.
        foreach (var item in backlog)
        {
            await work.ExecuteAsync($"UPDATE isk_work.items SET bucket = 0 WHERE item_id = '{item}'");
        }

        foreach (var (item, shard) in others.Select((item, index) => (item, index + 1)))
        {
            await work.ExecuteAsync($"UPDATE isk_work.items SET bucket = {shard} WHERE item_id = '{item}'");
        }

        var fields = WorkTestSettings.Fast + $",\"batchSize\":2,\"dispatch\":{{\"shards\":{Shards},\"concurrency\":4,\"scopeConcurrency\":1}}";
        await using (var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], fields))
        {
            dispatcher.Start();
            foreach (var item in backlog.Concat(others))
            {
                await Eventually.StateAsync(work, item, "completed");
            }
        }

        Assert.True(
            await work.ScalarAsync<bool>(
                $"SELECT (SELECT max(finished_at) FROM isk_work.items WHERE item_id IN ({Ids(others)})) "
                + $"< (SELECT max(finished_at) FROM isk_work.items WHERE item_id IN ({Ids(backlog)}))"),
            "The other shards waited for shard 0's backlog to drain.");
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

    private static string Ids(IEnumerable<Guid> items) => string.Join(", ", items.Select(item => $"'{item}'"));

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

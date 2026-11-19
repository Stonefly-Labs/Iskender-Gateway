using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Duplicate;

/// <summary>
/// Fencing a paused dispatcher (ISK-0009 T027, US2.2, as amended in <c>implement</c>): A pauses after its attempt record
/// past its lease's expiry, B takes the shard and completes the item, and A's apply is then rejected by the guard.
/// </summary>
public class FencingTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    [Fact]
    public async Task AResumedStaleHoldersApplyIsRejectedByTheGuardAndNothingItWritesPersists()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "fenced");
        var before = work.Logger.Events.Count;

        await using (var takeover = await Takeover.StartAsync(work))
        {
            takeover.HeldB.Release();
            await Eventually.StateAsync(work, item, "completed");

            takeover.HeldA.Release();
            await takeover.PassA;
        }

        var events = work.Logger.Events.Skip(before).ToList();
        var rejected = Assert.Single(events, logged => logged.Id.Id == 5006);
        Assert.Contains(rejected.Fields, field => field.Key == "Reason" && (string?)field.Value == "stale_token");
        Assert.Contains(events, logged => logged.Id.Id == 8010 && logged.Fields.Any(field => field.Key == "Outcome" && (string?)field.Value == WorkOutcomes.FencingRejected));
        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal(2, await work.ScalarAsync<int>($"SELECT attempt FROM isk_applies.applies WHERE item_id = '{item}'"));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    // A discard at the attempt record reports the attempt being made in 8010, not the count before it (ISK-0009 T092,
    // Diagnostics event 8010). Both items are in one shard, the second on its second attempt, and a newer token for the
    // shard's fence resource, written while the first item's apply has committed, makes the guard reject the second's
    // attempt record.
    [Fact]
    public async Task ADiscardAtTheAttemptRecordReportsTheAttemptBeingMade()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var first = await work.EnqueueAsync(scope, TestKinds.Record, "attempt-first");
        var second = await work.EnqueueAsync(scope, TestKinds.Record, "attempt-second");
        await work.ExecuteAsync($"UPDATE isk_work.items SET bucket = 0, due_at = clock_timestamp() - interval '1 minute' WHERE item_id = '{first}'");
        await work.ExecuteAsync($"UPDATE isk_work.items SET bucket = 0, attempt_count = 1 WHERE item_id = '{second}'");
        var before = work.Logger.Events.Count;

        await using (var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast))
        {
            var applied = dispatcher.Injection.At(WorkInjectionPoint.AfterApplyCommit);
            var pass = dispatcher.StartPass();
            Assert.Equal(first, await applied.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken));
            await work.ExecuteAsync("UPDATE isk_coordination.fences SET highest_token = highest_token + 1000");
            applied.Release();
            await pass;
        }

        var discarded = Assert.Single(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8010);
        Assert.Contains(discarded.Fields, field => field.Key == "Outcome" && (string?)field.Value == WorkOutcomes.FencingRejected);
        Assert.Contains(discarded.Fields, field => field.Key == "Attempt" && field.Value is 2);
        Assert.Equal("completed|1", await work.ItemAsync(first));
        Assert.Equal("pending|1", await work.ItemAsync(second));
        Assert.Equal(0L, await work.AppliesAsync(second));
    }
}

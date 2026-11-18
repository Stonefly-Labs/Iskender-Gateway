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
}

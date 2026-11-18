using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>
/// Handler timeouts and lease renewal (ISK-0009 T030, FR-014, <c>work</c> contract Dispatch and apply rules 3 and 8): a
/// hung handler is cancelled at its timeout and rolls back while the shard's other items run, a long handler keeps its
/// lease by renewal, one past a unit's 60 s default completes within its timeout, and a lost lease cancels the handler
/// and writes nothing more.
/// </summary>
public class TimeoutAndRenewalTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

    public static TheoryData<int, int?, int> Timeouts() => new()
    {
        // handler.timeout, the registration's Timeout, and the timeout that applies, in seconds.
        { 1, null, 1 },
        { 30, 1, 1 },
        { 1, 3, 3 },
    };

    [Theory]
    [MemberData(nameof(Timeouts))]
    public async Task AHungHandlerIsCancelledAtItsTimeoutAndRollsBackAsATransientTimeout(int handlerTimeout, int? registrationTimeout, int applies)
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), HangHandler.Kind, $"timeout-{handlerTimeout}-{registrationTimeout}");
        var registration = HangHandler.Registration(registrationTimeout is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        await using (var dispatcher = new TestDispatcher(work, [registration], WorkTestSettings.Fast + $",\"handler\":{{\"timeout\":\"{handlerTimeout}s\"}}"))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal(WorkClassifier.Timeout.Class.ToCode() + "|" + WorkClassifier.Timeout.Code.Id, await FailureAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        Assert.InRange(HangHandler.Ran[item], TimeSpan.FromSeconds(applies) - Tolerance, TimeSpan.FromSeconds(applies + 2));
        await FinishAsync(item);
    }

    [Fact]
    public async Task AHungHandlerStallsOnlyItsShardUntilItsTimeoutWhileTheScopesOtherShardsRun()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var hung = await work.EnqueueAsync(scope, HangHandler.Kind, "hung");
        var hungShard = ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(hung), 2);
        var beside = new List<Guid>();
        var behind = new List<Guid>();
        for (var index = 0; beside.Count < 2 || behind.Count < 2; index++)
        {
            var item = await work.EnqueueAsync(scope, TestKinds.Record, "shard-" + index);
            (ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), 2) == hungShard ? behind : beside).Add(item);
        }

        // The hung item's shard-mates fall due a second later, past the first claim, so the shard takes them only after the
        // hung handler's 3 s timeout ends its pass; the other shard's items are claimed and complete meanwhile.
        await work.ExecuteAsync($"UPDATE isk_work.items SET due_at = clock_timestamp() + interval '1 second' WHERE item_id IN ('{string.Join("','", behind)}')");
        await using (var dispatcher = new TestDispatcher(
            work,
            [HangHandler.Registration(TimeSpan.FromSeconds(3)), ApplyRecordingHandler.Registration()],
            WorkTestSettings.Fast + ",\"dispatch\":{\"shards\":2,\"concurrency\":4,\"scopeConcurrency\":4}"))
        {
            dispatcher.Start();
            foreach (var item in beside)
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            Assert.False(HangHandler.Ran.ContainsKey(hung), "The hung handler was cancelled before the other shard completed.");
            foreach (var item in behind)
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            Assert.True(HangHandler.Ran.ContainsKey(hung), "The hung handler's shard completed before its timeout.");
        }

        foreach (var item in beside.Concat(behind))
        {
            Assert.Equal(1L, await work.AppliesAsync(item));
        }

        await FinishAsync(hung);
    }

    [Fact]
    public async Task AHandlerThatOutlivesTheLeaseKeepsItByRenewalAndCompletes()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), SlowHandler.Kind, "slow");

        await using (var dispatcher = new TestDispatcher(work, [SlowHandler.Registration()], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.True(SlowHandler.Duration > TimeSpan.FromSeconds(5));
        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    // The apply unit is bounded by the handler's timeout and a margin, not by a unit's 60 s default, so a handler that runs
    // past 60 s within its timeout completes (ISK-0009 T065, FR-014, Dispatch and apply rule 8).
    [Fact]
    public async Task AHandlerThatRunsPastSixtySecondsWithinItsTimeoutCompletes()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), LongHandler.Kind, "long");

        await using (var dispatcher = new TestDispatcher(work, [LongHandler.Registration()], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal("none", await work.ScalarAsync<string>($"SELECT coalesce(last_failure_code, 'none') FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    [Fact]
    public async Task ALostLeaseCancelsTheHandlerCommitsNothingAndWritesNoFailureRecord()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), HangHandler.Kind, "lost");
        var before = work.Logger.Events.Count;

        await using (var dispatcher = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(1))], WorkTestSettings.Fast))
        {
            var applying = dispatcher.Injection.At(WorkInjectionPoint.AfterAttemptCommit, blocks: false);
            var pass = dispatcher.StartPass();
            await applying.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);

            // The lease row vanishes, so the next renewal fails with coordination.lease_lost.
            await work.ExecuteAsync("DELETE FROM isk_coordination.leases");
            await pass;
        }

        Assert.True(HangHandler.Ran.ContainsKey(item));
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal("abandoned|work.attempt_abandoned", await FailureAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        Assert.Contains(work.Logger.Events.Skip(before), logged => logged.Id.Id == 8010 && logged.Fields.Any(field => field.Key == "Outcome" && (string?)field.Value == WorkOutcomes.LeaseLost));
        Assert.DoesNotContain(work.Logger.Events.Skip(before), logged => logged.Id.Id is 8006 or 8007);
        await FinishAsync(item);
    }

    private Task<string> FailureAsync(Guid item) =>
        work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'");

    // Takes the test's item out of later tests' dispatch, so a hung kind is not retried by them.
    private Task FinishAsync(Guid item) =>
        work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}' AND state = 'pending'");
}

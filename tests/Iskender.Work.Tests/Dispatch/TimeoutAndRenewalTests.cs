using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>
/// Handler timeouts and lease renewal (ISK-0009 T030, FR-014, <c>work</c> contract Dispatch and apply rules 3 and 8): a
/// hung handler is cancelled at its timeout and rolls back while the shard's other items run, one hung inside a unit
/// statement is classified by the token that fired, a long handler keeps its
/// lease by renewal, one past a unit's 60 s default completes within its timeout, a lost lease cancels the handler
/// and writes nothing more, and a hung handler holds up no claim before its timeout.
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

    // The handler's timeout starts with the handler, so a slow open of the apply unit does not shorten it (Dispatch and
    // apply rule 8; maintainer decision in ISK-0009 implement, 2026-11-20).
    [Fact]
    public async Task ASlowApplyUnitOpenDoesNotShortenTheHandlersTimeout()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), HangHandler.Kind, "slow-open");

        await using (var dispatcher = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromSeconds(1))], WorkTestSettings.Fast))
        {
            dispatcher.Units.AfterOpen = (operation, token) => operation == "work.apply" ? Task.Delay(TimeSpan.FromMilliseconds(500), token) : Task.CompletedTask;
            await dispatcher.PassAsync();
        }

        Assert.Equal(WorkClassifier.Timeout.Class.ToCode() + "|" + WorkClassifier.Timeout.Code.Id, await FailureAsync(item));
        Assert.InRange(HangHandler.Ran[item], TimeSpan.FromSeconds(1) - Tolerance, TimeSpan.FromSeconds(3));
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

    // A shard's items claimed together run one after another, so an item waiting for its shard-mate's apply neither uses
    // up its handler's timeout nor fails an attempt (ISK-0009 T079, FR-014, Dispatch and apply rule 8).
    [Fact]
    public async Task AShardsItemsClaimedTogetherEachCompleteOnTheirFirstAttempt()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var items = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            items.Add(await work.EnqueueAsync(scope, PacedHandler.Kind, "paced-" + index));
        }

        await using (var dispatcher = new TestDispatcher(
            work, [PacedHandler.Registration()], WorkTestSettings.Fast + ",\"dispatch\":{\"shards\":1,\"concurrency\":4,\"scopeConcurrency\":4}"))
        {
            await dispatcher.PassAsync();
        }

        foreach (var item in items)
        {
            Assert.Equal("completed|1", await work.ItemAsync(item));
            Assert.Equal("none", await work.ScalarAsync<string>($"SELECT coalesce(last_failure_code, 'none') FROM isk_work.items WHERE item_id = '{item}'"));
            Assert.Equal(1L, await work.AppliesAsync(item));
        }
    }

    // A hung handler's shard-mates claimed in its pass wait for it without taking the scope's other slots, so the scope's
    // other shards complete before its timeout, and the shard-mates complete on their first attempt after it (ISK-0009 T079,
    // FR-016, spec Edge Cases). The hung item's shard is the first whose lease is tried, so its shard-mates would otherwise
    // take the slots before the later shards reserve theirs.
    [Fact]
    public async Task AHungHandlersShardMatesClaimedWithItLeaveTheScopesOtherShardsProgressing()
    {
        work.RequirePostgreSql();
        const int shards = 4;
        var scope = WorkFixture.FreshOrganization();
        Guid hung;
        while (true)
        {
            var candidate = await work.EnqueueAsync(scope, HangHandler.Kind, "hung-mates-" + Guid.NewGuid().ToString("N"));
            if (ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(candidate), shards) == 0)
            {
                hung = candidate;
                break;
            }

            await FinishAsync(candidate);
        }

        var mates = new List<Guid>();
        var others = new Dictionary<int, List<Guid>>();
        for (var index = 0; mates.Count < 2 || others.Count < shards - 1; index++)
        {
            var item = await work.EnqueueAsync(scope, TestKinds.Record, "mates-" + index);
            var shard = ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), shards);
            if (shard == 0)
            {
                mates.Add(item);
            }
            else
            {
                (others.TryGetValue(shard, out var list) ? list : others[shard] = []).Add(item);
            }
        }

        // The hung item is first in its shard's claim, so its shard-mates are claimed with it and wait behind it.
        await work.ExecuteAsync($"UPDATE isk_work.items SET due_at = clock_timestamp() - interval '1 minute' WHERE item_id = '{hung}'");
        await using (var dispatcher = new TestDispatcher(
            work,
            [HangHandler.Registration(TimeSpan.FromSeconds(10)), ApplyRecordingHandler.Registration()],
            WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":{shards},\"concurrency\":{shards},\"scopeConcurrency\":{shards}}}"))
        {
            dispatcher.Start();
            foreach (var item in others.Values.SelectMany(list => list))
            {
                await Eventually.StateAsync(work, item, "completed");
            }

            Assert.False(HangHandler.Ran.ContainsKey(hung), "The hung handler was cancelled before the other shards completed.");
            foreach (var item in mates)
            {
                await Eventually.StateAsync(work, item, "completed");
            }
        }

        foreach (var item in mates.Concat(others.Values.SelectMany(list => list)))
        {
            Assert.Equal("completed|1", await work.ItemAsync(item));
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

    // A handler awaiting a statement of its unit when its token fires fails with persistence.cancelled, and is still
    // classified by the token that fired: its timeout, or the shutdown (ISK-0009 T066, FR-014, Retry and poison rule 1).
    [Fact]
    public async Task AHandlerHungInAUnitStatementAtItsTimeoutIsATransientTimeout()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), StatementHangHandler.Kind, "statement-timeout");

        await using (var dispatcher = new TestDispatcher(work, [StatementHangHandler.Registration(TimeSpan.FromSeconds(1))], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal(ErrorCodes.PersistenceCancelled, Assert.IsType<GatewayException>(StatementHangHandler.Failed[item]).Code);
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal(WorkClassifier.Timeout.Class.ToCode() + "|" + WorkClassifier.Timeout.Code.Id, await FailureAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        await FinishAsync(item);
    }

    [Fact]
    public async Task AHandlerHungInAUnitStatementAtShutdownIsATransientCancellation()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), StatementHangHandler.Kind, "statement-shutdown");

        await using (var dispatcher = new TestDispatcher(work, [StatementHangHandler.Registration(TimeSpan.FromMinutes(1))], WorkTestSettings.Fast))
        {
            var pass = dispatcher.StartPass();
            await Eventually.HoldsAsync(
                async () => await work.ScalarAsync<long>(
                    $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{work.DatabaseName}' AND state = 'active' "
                    + "AND query LIKE '%pg_sleep(600)%' AND pid <> pg_backend_pid()") > 0,
                "the handler's statement");
            await dispatcher.StopAsync();
            await pass;
        }

        Assert.Equal(ErrorCodes.PersistenceCancelled, Assert.IsType<GatewayException>(StatementHangHandler.Failed[item]).Code);

        // A cancellation at shutdown hands its attempt back (Retry and poison rule 4).
        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.Equal(WorkClassifier.Shutdown.Class.ToCode() + "|" + WorkClassifier.Shutdown.Code.Id, await FailureAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        await FinishAsync(item);
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

    // A hung handler holds up no claim until its timeout: an item enqueued after it started, in another shard of its scope,
    // completes within a few poll intervals, long before the hung handler's timeout cancels it (ISK-0009 T084, FR-018,
    // spec Edge Cases "a handler hangs").
    [Fact]
    public async Task AnItemEnqueuedWhileAHandlerHangsCompletesBeforeItsTimeout()
    {
        work.RequirePostgreSql();
        const int shards = 2;
        var scope = WorkFixture.FreshOrganization();
        var item = await work.EnqueueAsync(scope, HangHandler.Kind, "hang-before-timeout");
        var hungShard = ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), shards);
        var waiting = new List<Guid> { item };
        var fields = WorkTestSettings.Fast + $",\"dispatch\":{{\"shards\":{shards},\"scopeConcurrency\":2}}";
        var completing = System.Diagnostics.Stopwatch.StartNew();
        await using (var dispatcher = new TestDispatcher(work, [HangHandler.Registration(TimeSpan.FromMinutes(1)), ApplyRecordingHandler.Registration()], fields))
        {
            var applying = dispatcher.Injection.At(WorkInjectionPoint.AfterAttemptCommit, blocks: false);
            dispatcher.Start();
            await applying.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);

            Guid later;
            for (var index = 0; ; index++)
            {
                later = await work.EnqueueAsync(scope, TestKinds.Record, "after-hang-" + index);
                if (ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(later), shards) != hungShard)
                {
                    break;
                }

                waiting.Add(later);
            }

            completing.Restart();
            await Eventually.StateAsync(work, later, "completed");
            completing.Stop();
            Assert.False(HangHandler.Ran.ContainsKey(item), "The hung handler ended before the later item completed.");
        }

        Assert.InRange(completing.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        foreach (var held in waiting)
        {
            await FinishAsync(held);
        }
    }

    private Task<string> FailureAsync(Guid item) =>
        work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'");

    // Takes the test's item out of later tests' dispatch, so a hung kind is not retried by them.
    private Task FinishAsync(Guid item) =>
        work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}' AND state = 'pending'");
}

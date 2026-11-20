using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>
/// Apply once (ISK-0009 T023, US1, FR-010, FR-012, FR-016): an item is applied exactly once, whether its dispatcher
/// finishes or is abandoned at any injection point of an attempt, and another dispatcher takes over after the lease expires.
/// </summary>
public class ApplyOnceTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    // The shortest shard lease, 5 s, and short retries, so a taken-over item is due again before the abandoned lease expires.
    private const string Fast = "\"leaseDuration\":\"5s\",\"retry\":{\"initialDelay\":\"100ms\",\"maximumDelay\":\"1s\"}";

    [Fact]
    public async Task ACommittedItemIsDeliveredToItsHandlerOnceAndEndsCompletedWithOneApply()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.ProjectA1, TestKinds.Record, "once");

        await using var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], Fast);
        await dispatcher.PassAsync();

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
        var delivery = Assert.Single(ApplyRecordingHandler.Deliveries, delivery => delivery.ItemId == item);
        Assert.Equal((1, "once", TestKinds.Record), (delivery.Attempt, delivery.IdempotencyKey, delivery.Kind));
        Assert.Equal(WorkFixture.ProjectA1.Owner, delivery.Owner);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("poisoned")]
    public async Task AFinishedItemIsNeverDelivered(string state)
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.OrganizationB, TestKinds.Record, "finished-" + state);
        await work.ExecuteAsync($"UPDATE isk_work.items SET state = '{state}', finished_at = clock_timestamp() WHERE item_id = '{item}'");

        await using var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], Fast);
        await dispatcher.PassAsync();

        Assert.Equal(state + "|0", await work.ItemAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        Assert.DoesNotContain(ApplyRecordingHandler.Deliveries, delivery => delivery.ItemId == item);
    }

    [Fact]
    public async Task AbandonmentAfterTheApplysCommitLeavesTheItemCompletedAndARestartDoesNotDeliverItAgain()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.OrganizationA, TestKinds.Record, "after-apply");

        await AbandonAtAsync(WorkInjectionPoint.AfterApplyCommit, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("completed|1", await work.ItemAsync(item));

        await using var restarted = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], Fast);
        await restarted.PassAsync();

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    [Fact]
    public async Task AbandonmentDuringTheHandlerRollsBackAndAnotherDispatcherAppliesItOnceAfterTheLeaseExpires()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.OrganizationA, TestKinds.Record, "during-handler");

        await AbandonAtAsync(WorkInjectionPoint.DuringHandler, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));

        await DrainAsync(item, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    [Fact]
    public async Task AbandonmentBeforeTheAttemptRecordsCommitCountsNoAttempt()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.ProjectA1, TestKinds.Record, "before-attempt");

        await AbandonAtAsync(WorkInjectionPoint.BeforeAttemptCommit, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("pending|0", await work.ItemAsync(item));

        await DrainAsync(item, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    [Fact]
    public async Task AbandonmentAfterTheAttemptRecordsCommitCountsTheAttempt()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.ProjectA1, TestKinds.Record, "after-attempt");

        await AbandonAtAsync(WorkInjectionPoint.AfterAttemptCommit, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));

        await DrainAsync(item, [ApplyRecordingHandler.Registration()]);
        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    [Theory]
    [InlineData(false, "abandoned|work.attempt_abandoned")]
    [InlineData(true, "transient|persistence.timeout")]
    public async Task AbandonmentAroundTheFailureRecordsCommitEndsCompletedWithOneApply(bool afterCommit, string recorded)
    {
        var point = afterCommit ? WorkInjectionPoint.AfterFailureCommit : WorkInjectionPoint.BeforeFailureCommit;
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.OrganizationB, TestKinds.FailOnce, "failure-" + point);

        await AbandonAtAsync(point, [FailOnceHandler.Registration()]);
        Assert.Equal("pending|1", await work.ItemAsync(item));
        Assert.Equal(recorded, await work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'"));

        await DrainAsync(item, [FailOnceHandler.Registration()]);
        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    // Runs one pass of a dispatcher that blocks at point, abandons it there, and lets the pass's remains end.
    private async Task AbandonAtAsync(WorkInjectionPoint point, IReadOnlyList<WorkHandlerRegistration> handlers)
    {
        var abandoned = new TestDispatcher(work, handlers, Fast);
        var hook = abandoned.Injection.At(point);
        var pass = abandoned.StartPass();
        await hook.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        await abandoned.AbandonAsync();
        await pass.ContinueWith(_ => { }, TaskScheduler.Default);
        await abandoned.DisposeAsync();
    }

    // Passes a fresh dispatcher until item is finished: its shard is contended until the abandoned lease expires.
    private async Task DrainAsync(Guid item, IReadOnlyList<WorkHandlerRegistration> handlers)
    {
        await using var successor = new TestDispatcher(work, handlers, Fast);
        await Eventually.HoldsAsync(
            async () =>
            {
                await successor.PassAsync();
                return (await work.ItemAsync(item)).StartsWith("completed", StringComparison.Ordinal);
            },
            "the successor to complete the item");
    }
}

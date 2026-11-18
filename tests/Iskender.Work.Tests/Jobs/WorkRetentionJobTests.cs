using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Jobs;

/// <summary>
/// The shipped job <c>work.retention</c> (ISK-0009 T043, FR-021, <c>work</c> contract Recurring jobs rule 5 and The outbox
/// rule 6): completed and poisoned items past their retention go and recent and pending ones stay, a scope with no item
/// left loses its <c>work</c> directory row, and afterwards the same idempotency key enqueues a new item.
/// </summary>
public class WorkRetentionJobTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OldItemsGoRecentOnesStayAnEmptiedScopeLosesItsRowAndItsKeyEnqueuesAgain()
    {
        work.RequirePostgreSql();
        var (kept, emptied) = (WorkFixture.FreshOrganization(), WorkFixture.FreshOrganization());
        var oldCompleted = await FinishedAsync(kept, "old-completed", "completed", "8 days");
        var recentCompleted = await FinishedAsync(kept, "recent-completed", "completed", "1 hour");
        var oldPoisoned = await FinishedAsync(kept, "old-poisoned", "poisoned", "31 days");
        var recentPoisoned = await FinishedAsync(kept, "recent-poisoned", "poisoned", "2 days");
        var pending = await work.EnqueueAsync(kept, TestKinds.Record, "pending");
        var gone = await FinishedAsync(emptied, "only", "completed", "8 days");
        var (keptListed, emptiedListed) = (await ListedAsync(pending), await ListedAsync(gone));

        // Default retention: 7 days for completed items, 30 for poisoned ones.
        var (settings, _) = WorkTestSettings.Read();
        await using var sources = new PostgreSqlDataSources(TestSettings.For(work.Database), TestCredentialProvider.For(work.Postgres));
        var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, work.Logger);
        var outcome = await new WorkRetentionJob(units, settings).RunAsync(new RecurringJobContext(1, null, (_, _) => ValueTask.CompletedTask), Token);

        Assert.Equal(RecurringJobOutcome.Completed, outcome);
        foreach (var item in new[] { oldCompleted, oldPoisoned, gone })
        {
            Assert.Equal("absent", await work.ItemAsync(item));
        }

        Assert.Equal("completed|1", await work.ItemAsync(recentCompleted));
        Assert.Equal("poisoned|1", await work.ItemAsync(recentPoisoned));
        Assert.Equal("pending|0", await work.ItemAsync(pending));
        Assert.Equal(1L, await RowsAsync(keptListed));
        Assert.Equal(0L, await RowsAsync(emptiedListed));

        var again = await work.EnqueueAsync(emptied, TestKinds.Record, "only");
        Assert.NotEqual(gone, again);
        Assert.Equal("pending|0", await work.ItemAsync(again));
        Assert.Equal(1L, await RowsAsync(emptiedListed));
    }

    // Enqueues an item in scope and marks it state, finished age ago.
    private async Task<Guid> FinishedAsync(UnitOfWorkScope scope, string key, string state, string age)
    {
        var item = await work.EnqueueAsync(scope, TestKinds.Record, key);
        await work.ExecuteAsync($"UPDATE isk_work.items SET state = '{state}', attempt_count = 1, finished_at = clock_timestamp() - interval '{age}' WHERE item_id = '{item}'");
        return item;
    }

    private Task<string> ListedAsync(Guid item) => work.ScalarAsync<string>($"SELECT scope_key FROM isk_work.items WHERE item_id = '{item}'");

    private Task<long> RowsAsync(string listed) =>
        work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND listed_scope = '{listed}'");
}

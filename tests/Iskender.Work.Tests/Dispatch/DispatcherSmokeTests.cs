using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>One pass of the dispatch cycle end to end (ISK-0009 T020): claim, attempt record, apply, completion, settle.</summary>
public class DispatcherSmokeTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APassCompletesADueItemOnceAndRemovesTheScopesDirectoryRow()
    {
        work.RequirePostgreSql();
        var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(TestKinds.Record, 1)], logger: null);
        Guid item;
        await using (var unit = await work.OpenAsync(WorkFixture.OrganizationA))
        {
            item = (await outbox.EnqueueAsync(unit, new WorkItemRequest(WorkFixture.OrganizationA.Owner, TestKinds.Record, 1, new byte[] { 1 }, "smoke"), Token)).ItemId;
            await unit.CommitAsync(Token);
        }

        await using var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()]);
        await dispatcher.PassAsync();

        Assert.Equal("completed|1||", await work.ScalarAsync<string>($"SELECT state || '|' || attempt_count || '|' || coalesce(last_failure_class, '') || '|' || coalesce(last_failure_code, '') FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_applies.applies WHERE item_id = '{item}'"));
        Assert.Equal("t", await work.ScalarAsync<string>($"SELECT CASE WHEN due_at IS NULL THEN 't' ELSE 'f' END FROM isk_directory.scopes WHERE module = 'work' AND listed_scope = 'org:{Owners.OrgA}'"));

        await dispatcher.PassAsync();
        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_applies.applies WHERE item_id = '{item}'"));
    }
}

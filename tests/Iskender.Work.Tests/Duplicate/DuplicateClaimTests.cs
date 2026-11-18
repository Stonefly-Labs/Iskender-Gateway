using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Duplicate;

/// <summary>A duplicate claim (ISK-0009 T026, US2.1, FR-013): two dispatchers hold one item, and at most one apply commits.</summary>
public class DuplicateClaimTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    [Fact]
    public async Task TwoDispatchersReleasedTogetherCommitOneApplyAndTheOtherWrites8010()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "duplicate");
        var discarded = Discarded();

        await using (var takeover = await Takeover.StartAsync(work))
        {
            Assert.Equal("pending|2", await work.ItemAsync(item));
            takeover.HeldA.Release();
            takeover.HeldB.Release();
            await takeover.PassA;
            await Eventually.StateAsync(work, item, "completed");
        }

        Assert.Equal("completed|2", await work.ItemAsync(item));
        Assert.Equal(1L, await work.AppliesAsync(item));
        var outcome = Assert.Single(Discarded().Skip(discarded.Count));
        Assert.Contains(outcome, new[] { WorkOutcomes.FencingRejected, WorkOutcomes.Superseded });
    }

    private List<string> Discarded() =>
        [.. work.Logger.Events.Where(logged => logged.Id.Id == 8010).Select(logged => (string)logged.Fields.Single(field => field.Key == "Outcome").Value!)];
}

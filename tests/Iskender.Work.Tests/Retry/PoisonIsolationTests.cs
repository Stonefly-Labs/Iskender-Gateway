using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Retry;

/// <summary>
/// Poison isolation (ISK-0009 T035, US3.4, FR-018, <c>work</c> contract Retry and poison rule 7): a poisoned item and a
/// failing-every-attempt item hold up nothing else, and an unreachable database counts no attempt, poisons nothing,
/// retries at <c>pollInterval</c>, and writes 8012 once per change of class and code, and again after a pass succeeds.
/// </summary>
public class PoisonIsolationTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    [Fact]
    public async Task APoisonedAndAFailingItemLeaveTheirScopesOtherItemsAndOtherScopesDispatching()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var poisoned = await work.EnqueueAsync(scope, TestKinds.Record, "poisoned");
        await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{poisoned}'");
        var failing = await work.EnqueueAsync(scope, AlwaysFailingHandler.Kind, "failing");
        var normal = new List<Guid>();
        foreach (var target in new[] { scope, scope, WorkFixture.FreshOrganization(), WorkFixture.ProjectA1 })
        {
            normal.Add(await work.EnqueueAsync(target, TestKinds.Record, "normal-" + normal.Count));
        }

        await using (var dispatcher = new TestDispatcher(
            work,
            [ApplyRecordingHandler.Registration(), AlwaysFailingHandler.Registration()],
            "\"pollInterval\":\"1s\",\"leaseDuration\":\"5s\",\"retry\":{\"initialDelay\":\"100ms\",\"maximumDelay\":\"1s\",\"maximumAttempts\":100}"))
        {
            dispatcher.Start();
            foreach (var item in normal)
            {
                await Eventually.StateAsync(work, item, "completed");
            }
        }

        Assert.Equal("poisoned|0", await work.ItemAsync(poisoned));
        Assert.StartsWith("pending|", await work.ItemAsync(failing), StringComparison.Ordinal);
        Assert.Equal(0L, await work.AppliesAsync(poisoned));
        await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{failing}'");
    }

    [Fact]
    public async Task AnUnreachableDatabaseCountsNoAttemptAndWrites8012OncePerChangeAndAgainAfterASuccess()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        await using var proxy = new OutageProxy(work.Postgres.Host, work.Postgres.Port);
        await using var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast, proxy);
        var before = work.Logger.Events.Count;
        dispatcher.Start();
        var first = await work.EnqueueAsync(scope, TestKinds.Record, "before-outage");
        await Eventually.StateAsync(work, first, "completed");

        // While down, each poll fails to connect, at pollInterval, and only the first failure writes 8012.
        proxy.Down = true;
        var during = await work.EnqueueAsync(scope, TestKinds.Record, "during-outage");
        var failed = Count(4001);
        var started = TimeProvider.System.GetTimestamp();
        await Eventually.HoldsAsync(() => Task.FromResult(Count(4001) >= failed + 3), "three failed polls");
        Assert.InRange(TimeProvider.System.GetElapsedTime(started), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(10));
        var passFailures = PassFailures(before);
        var failure = Assert.Single(passFailures);
        Assert.Contains(failure.Fields, field => field.Key == "Code" && field.Value?.ToString() == "persistence.unavailable");
        Assert.Equal("pending|0", await work.ItemAsync(during));

        // A pass that succeeds, then a second outage, writes 8012 again.
        proxy.Down = false;
        await Eventually.StateAsync(work, during, "completed");
        Assert.Equal("completed|1", await work.ItemAsync(during));
        proxy.Down = true;
        await Eventually.HoldsAsync(() => Task.FromResult(PassFailures(before).Count == 2), "8012 after the success");
        proxy.Down = false;

        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE state = 'poisoned' AND item_id IN ('{first}', '{during}')"));
    }

    private List<CapturedLog> PassFailures(int before) => [.. work.Logger.Events.Skip(before).Where(logged => logged.Id.Id == 8012)];

    private int Count(int eventId) => work.Logger.Events.Count(logged => logged.Id.Id == eventId);

    private sealed class AlwaysFailingHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "always_failing");

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(AlwaysFailingHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("always");
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
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

        // A pass that succeeds, then a second outage, writes 8012 again. The item completes inside a pass, so the outage
        // waits for the next pass's directory page, which starts only after that pass has ended and reset the failure.
        proxy.Down = false;
        await Eventually.StateAsync(work, during, "completed");
        Assert.Equal("completed|1", await work.ItemAsync(during));
        var pages = Pages(dispatcher);
        await Eventually.HoldsAsync(() => Task.FromResult(Pages(dispatcher) > pages), "a pass after the success");
        proxy.Down = true;
        await Eventually.HoldsAsync(() => Task.FromResult(PassFailures(before).Count == 2), "8012 after the success");
        proxy.Down = false;

        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE state = 'poisoned' AND item_id IN ('{first}', '{during}')"));
    }

    // An item's unit failure, here at its attempt record on every pass, writes 8012 once: the passes around it succeed,
    // and do not reset it (ISK-0009 T071, Diagnostics rule 2).
    [Fact]
    public async Task APersistentItemFailureWrites8012OnceWhileThePassesSucceed()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "item-failure");
        await using var dispatcher = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        var failing = dispatcher.Injection.At(WorkInjectionPoint.BeforeAttemptCommit);
        failing.Throw(new GatewayException(ErrorCodes.PersistenceUnavailable));
        var before = work.Logger.Events.Count;

        dispatcher.Start();
        await failing.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        var pages = Pages(dispatcher);
        await Eventually.HoldsAsync(() => Task.FromResult(Pages(dispatcher) >= pages + 3 && failing.Count >= 3), "three more passes over the failing item");
        await dispatcher.StopAsync();

        var failure = Assert.Single(PassFailures(before));
        Assert.Contains(failure.Fields, field => field.Key == "Code" && field.Value?.ToString() == "persistence.unavailable");
        Assert.Equal("pending|0", await work.ItemAsync(item));
        await work.ExecuteAsync($"UPDATE isk_work.items SET state = 'poisoned', finished_at = clock_timestamp() WHERE item_id = '{item}'");
    }

    // A lease acquire that fails after another shard has started lets that shard finish before the pass fails, so no
    // started item is left running past the pass (ISK-0009 T071).
    [Fact]
    public async Task AFailedLeaseAcquireAwaitsTheShardsAlreadyStarted()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var byShard = new Guid?[2];
        for (var index = 0; byShard.Any(item => item is null); index++)
        {
            var item = await work.EnqueueAsync(scope, TestKinds.Record, "acquire-" + index);
            byShard[ShardLeases.ShardOf(PostgreSqlWorkOutbox.Bucket(item), 2)] ??= item;
        }

        await using var dispatcher = new TestDispatcher(
            work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast + ",\"dispatch\":{\"shards\":2,\"concurrency\":4,\"scopeConcurrency\":4}");
        var held = dispatcher.Injection.At(WorkInjectionPoint.DuringHandler);
        var acquireFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Shards are taken in order: shard 0 starts and is held in its handler, then shard 1's acquire fails. The hold is
        // released only once shard 0 has reached it, after the failure, so a pass that fails before it is released did
        // not wait for shard 0.
        dispatcher.Leases.FailAcquire = name =>
        {
            if (name.Name != "dispatch.1")
            {
                return null;
            }

            acquireFailed.TrySetResult();
            return new GatewayException(ErrorCodes.PersistenceUnavailable);
        };

        var pass = dispatcher.StartPass();
        var releasedAtFailure = pass.ContinueWith(_ => released.Task.IsCompleted, TaskScheduler.Default);
        await held.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        await acquireFailed.Task.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        released.SetResult();
        held.Release();

        var failed = await Assert.ThrowsAsync<GatewayException>(() => pass);
        Assert.Same(ErrorCodes.PersistenceUnavailable, failed.Code);
        Assert.True(await releasedAtFailure, "The pass failed while shard 0's item was still running.");
        Assert.Equal("completed|1", await work.ItemAsync(byShard[0]!.Value));
        Assert.Equal("pending|0", await work.ItemAsync(byShard[1]!.Value));
    }

    private List<CapturedLog> PassFailures(int before) => [.. work.Logger.Events.Skip(before).Where(logged => logged.Id.Id == 8012)];

    private int Count(int eventId) => work.Logger.Events.Count(logged => logged.Id.Id == eventId);

    private static int Pages(TestDispatcher dispatcher) => dispatcher.Units.Opened.Count(opened => opened.Operation == "work.directory_page");

    private sealed class AlwaysFailingHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "always_failing");

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(AlwaysFailingHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("always");
    }
}

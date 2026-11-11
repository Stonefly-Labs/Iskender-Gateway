using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

namespace Iskender.Infrastructure.PostgreSql.Tests.Concurrency;

/// <summary>User Story 2 scenarios 2 and 3 and FR-014: the retry helper re-runs a whole serializable unit only when it is safe.</summary>
public class SerializableRetryTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private const int Writers = 20;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentSafeIncrementsAreNeitherLostNorDoubled()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["poolMaximum"] = Writers + 5 });
        var id = await SeedAsync(store);
        var counters = new CounterRepository();
        var runs = new int[Writers];
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var outcomes = Enumerable.Range(0, Writers).Select(writer => Task.Run(async () =>
        {
            await start.Task;
            try
            {
                await UnitOfWorkRetry.RunAsync(
                    store.Factory,
                    Owners.Organization(Owners.OrgA),
                    Options(safe: true),
                    async (unit, token) =>
                    {
                        Interlocked.Increment(ref runs[writer]);
                        return await counters.IncrementAsync(unit, id, token);
                    },
                    Token,
                    logger: store.Logger);
                return (GatewayException?)null;
            }
            catch (GatewayException failure)
            {
                return failure;
            }
        }, Token)).ToList();
        start.SetResult();
        var failures = await Task.WhenAll(outcomes);

        var exhausted = Enumerable.Range(0, Writers).Where(writer => failures[writer] is not null).ToList();
        Assert.All(exhausted, writer =>
        {
            Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, failures[writer]!.Code);
            Assert.Equal(UnitOfWorkRetry.MaxAttempts, runs[writer]);
        });
        Assert.All(runs, count => Assert.InRange(count, 1, UnitOfWorkRetry.MaxAttempts));
        Assert.Equal(Writers - exhausted.Count, await ValueAsync(store, id));
        Assert.True(runs.Sum() > Writers, "No writer conflicted, so the retry path was not exercised.");
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4005);
    }

    [Fact]
    public async Task ASerializationFailureNotMarkedSafeIsNotRetried()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var id = await SeedAsync(store);
        var counters = new CounterRepository();
        var runs = 0;

        await using var first = await store.OpenAsync(Owners.Organization(Owners.OrgA), TestStore.Options(isolation: UnitOfWorkIsolation.Serializable));
        Assert.Equal(1, await counters.IncrementAsync(first, id, Token));

        var second = UnitOfWorkRetry.RunAsync(
            store.Factory,
            Owners.Organization(Owners.OrgA),
            Options(safe: false),
            async (unit, token) =>
            {
                Interlocked.Increment(ref runs);
                return await counters.IncrementAsync(unit, id, token);
            },
            Token).AsTask();
        await WaitForLockWaitAsync();
        await first.CommitAsync(Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => second);
        Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, failure.Code);
        Assert.Equal(1, runs);
        Assert.Equal(1, await ValueAsync(store, id));
        Assert.DoesNotContain(store.Logger.Events, logged => logged.Id.Id == 4005);
    }

    private static UnitOfWorkOptions Options(bool safe) =>
        new("beta", "counter.increment") { Isolation = UnitOfWorkIsolation.Serializable, SafeToRerun = safe };

    private async Task WaitForLockWaitAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            if (await database.Fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") > 0)
            {
                return;
            }

            await Task.Delay(25, Token);
        }

        Assert.Fail("The second unit never waited on the first's row lock.");
    }

    private static async Task<string> SeedAsync(TestStore store)
    {
        var id = $"c-ser-{Guid.NewGuid():N}";
        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        await CounterRepository.CreateAsync(unit, id, Token);
        await unit.CommitAsync(Token);
        return id;
    }

    private static async Task<long> ValueAsync(TestStore store, string id)
    {
        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        return await new CounterRepository().GetAsync(unit, id, Token);
    }
}

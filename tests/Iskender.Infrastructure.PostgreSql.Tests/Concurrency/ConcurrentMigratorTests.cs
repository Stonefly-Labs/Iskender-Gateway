using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Concurrency;

/// <summary>User Story 2 scenario 6: two migrators started together apply each migration exactly once.</summary>
public class ConcurrentMigratorTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("30s")]
    [InlineData("0s")]
    public async Task TwoRunnersStartedTogetherApplyEachMigrationOnce(string lockWait)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var first = TestStore.For(database, TestSettings.MigrationWithLockWait(lockWait));
        await using var second = TestStore.For(database, TestSettings.MigrationWithLockWait(lockWait));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runs = new[] { first, second }.Select(store => Task.Run(async () =>
        {
            await start.Task;
            try
            {
                return new Outcome((await store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token)).Applied.Count, null);
            }
            catch (GatewayException failure)
            {
                return new Outcome(0, failure);
            }
        }, Token)).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(runs);

        Assert.Single(outcomes, outcome => outcome.Failure is null && outcome.Applied == 6);
        var other = Assert.Single(outcomes, outcome => !(outcome.Failure is null && outcome.Applied == 6));
        Assert.True(
            (other.Failure is null && other.Applied == 0) || other.Failure?.Code == ErrorCodes.PersistenceMigrationLocked,
            $"The second runner applied {other.Applied} or failed with {other.Failure?.Code.Id}.");
        Assert.Equal(6, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history"));
        Assert.Equal(1, await fixture.ScalarAsync<long>(database.Name, "SELECT max(n) FROM (SELECT count(*) AS n FROM isk_migrations.history GROUP BY module, version) per_identity"));
    }

    private sealed record Outcome(int Applied, GatewayException? Failure);
}

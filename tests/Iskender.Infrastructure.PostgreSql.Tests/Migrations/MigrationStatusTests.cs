using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>GetStatusAsync and the default reverse step (FR-023). The round trip and failures are in their own classes.</summary>
public class MigrationStatusTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmptyDatabaseHasEveryMigrationPendingAndChangesNothing()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        var status = await store.Runner(FixtureModules.Release1).GetStatusAsync(Token);

        Assert.Equal(["alpha", "beta"], status.Select(module => module.Module));
        Assert.All(status, module => Assert.Null(module.SchemaVersion));
        Assert.Equal([1, 2, 3], status[0].Pending.Select(migration => migration.Version));
        Assert.Equal(FixtureModules.AlphaRange, status[0].SupportedRange);
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regnamespace('isk_migrations') IS NULL"));
    }

    [Fact]
    public async Task StatusReportsSchemaVersionPendingUnknownAndMismatches()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(FixtureModules.Release2).ApplyAsync(new MigrationApplyRequest(), Token);

        var release2 = await store.Runner(FixtureModules.Release2).GetStatusAsync(Token);
        var release1 = await store.Runner(FixtureModules.Release1).GetStatusAsync(Token);
        var edited = await store.Runner(new MigrationSet([FixtureModules.AlphaEdited])).GetStatusAsync(Token);
        var next = await store.Runner(new MigrationSet([FixtureModules.AlphaNext, FixtureModules.Beta])).GetStatusAsync(Token);

        Assert.Equal([4, 3], release2.Select(module => module.SchemaVersion));
        Assert.All(release2, module => Assert.Empty(module.Pending.Concat(module.Unknown).Concat(module.ChecksumMismatches)));
        Assert.Equal([new MigrationIdentity("alpha", 4)], release1[0].Unknown);
        Assert.Equal(4, release1[0].SchemaVersion);
        Assert.Equal([new MigrationIdentity("alpha", 1)], Assert.Single(edited).ChecksumMismatches);
        Assert.Equal([new MigrationIdentity("alpha", 6)], next[0].Pending);
    }

    [Fact]
    public async Task ReverseTakesOneStepByDefaultAndKeepsTheSchema()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(FixtureModules.Release1);
        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        var reversed = await runner.ReverseAsync(new MigrationReverseRequest(), Token);
        var all = await runner.ReverseAsync(new MigrationReverseRequest { Steps = 10, ConfirmDataLoss = true }, Token);

        Assert.Equal([new MigrationIdentity("beta", 3)], reversed.Reversed);
        Assert.Empty(reversed.Applied);
        Assert.Equal(["beta/2", "beta/1", "alpha/3", "alpha/2", "alpha/1"], all.Reversed.Select(migration => migration.ToString()));
        Assert.All(await runner.GetStatusAsync(Token), module => Assert.Null(module.SchemaVersion));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regnamespace('isk_alpha') IS NOT NULL AND to_regnamespace('isk_beta') IS NOT NULL"));
        Assert.Empty((await runner.ReverseAsync(new MigrationReverseRequest(), Token)).Reversed);
    }

    [Fact]
    public async Task AnUnconfirmedDataLosingStepCarriesItsModuleAndVersion()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(new MigrationSet([FixtureModules.AlphaRelease1]));
        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, failure.Code);
        var values = failure.Error!.Diagnostics.Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(new Dictionary<string, string> { ["module"] = "alpha", ["version"] = "3" }, values);
        Assert.Equal(3, (await runner.GetStatusAsync(Token))[0].SchemaVersion);
    }

    [Fact]
    public async Task StatusNeedsTheMigrationCredentials()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database, withMigration: false), TestCredentialProvider.For(fixture));

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await new PostgreSqlMigrationRunner(sources, FixtureModules.Release1).GetStatusAsync(Token));

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
    }
}

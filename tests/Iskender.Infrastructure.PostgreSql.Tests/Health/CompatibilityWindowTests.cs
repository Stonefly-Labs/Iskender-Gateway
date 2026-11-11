using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

namespace Iskender.Infrastructure.PostgreSql.Tests.Health;

/// <summary>
/// Release 1 keeps working on a database migrated by release 2 (User Story 4 scenario 5, FR-024, research R22). Release 1
/// is Alpha 1 to 3 and Beta 1 to 3 with Alpha's range <c>[1, 5]</c>; release 2 adds the expand-only Alpha 4, and the
/// fixture-only Alpha 6 is beyond <c>Maximum</c>.
/// </summary>
public class CompatibilityWindowTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReleaseOneQueriesAndProbePassAfterReleaseTwoMigrates()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var store = TestStore.For(database);

        var applied = await store.Runner(FixtureModules.Release2).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal([new MigrationIdentity("alpha", 4)], applied.Applied);
        await RunReleaseOneQueriesAsync(store);
        Assert.Equal(PersistenceHealthResult.Healthy, await ReleaseOneProbe(store).CheckAsync(Token));
        var refusal = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, refusal.Code);
    }

    [Fact]
    public async Task ReleaseOneReportsSchemaAheadBeyondItsMaximum()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var store = TestStore.For(database);
        await store.Runner(FixtureModules.Release2).ApplyAsync(new MigrationApplyRequest(), Token);

        await store.Runner(new MigrationSet([FixtureModules.AlphaNext, FixtureModules.Beta])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(PersistenceHealthResult.Unhealthy(PersistenceHealthReason.SchemaAhead), await ReleaseOneProbe(store).CheckAsync(Token));
    }

    private static PostgreSqlHealthProbe ReleaseOneProbe(TestStore store) => new(store.Sources, FixtureModules.Release1, TimeProvider.System, store.Logger);

    // Every release-1 repository query from T028: insert, read, list, update, and delete in each fixture table.
    private static async Task RunReleaseOneQueriesAsync(TestStore store)
    {
        var widgets = new WidgetRepository();
        var notes = new WidgetNoteRepository();
        var counters = new CounterRepository();

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            await WidgetRepository.InsertAsync(unit, "w-compat-1", "one", Token);
            await WidgetRepository.InsertAsync(unit, "w-compat-2", "two", Token);
            Assert.Equal(new Widget("w-compat-1", Owners.OrgA.Value, "one", 1), await widgets.GetAsync(unit, "w-compat-1", Token));
            Assert.Equal(2, (await widgets.ListAsync(unit, Token)).Count(widget => widget.Id.StartsWith("w-compat-", StringComparison.Ordinal)));
            Assert.Equal(1, await widgets.RenameAsync(unit, "w-compat-1", "uno", Token));
            var version = await WidgetRepository.RenameVersionedAsync(unit, "w-compat-1", new VersionToken(2), "eins", Token);
            await WidgetRepository.DeleteVersionedAsync(unit, "w-compat-1", version, Token);
            await CounterRepository.CreateAsync(unit, "c-compat", Token);
            Assert.Equal(1, await counters.IncrementAsync(unit, "c-compat", Token));
            Assert.Equal(1, await counters.GetAsync(unit, "c-compat", Token));
            Assert.Equal(1, await counters.DeleteAsync(unit, "c-compat", Token));
            await LockLogRepository.AppendAsync(unit, "compat", Token);
            Assert.Equal(["compat"], await LockLogRepository.ReadAsync(unit, Token));
            await unit.CommitAsync(Token);
        }

        await using (var project = await store.OpenAsync(Owners.Project(Owners.OrgA, Owners.ProjectA1)))
        {
            await notes.AddAsync(project, "n-compat", "w-compat-2", "hello", Token);
            Assert.Equal("hello", (await notes.GetAsync(project, "n-compat", Token)).Body);
            Assert.Single(await notes.ListAsync(project, Token));
            Assert.Equal(1, await notes.UpdateAsync(project, "n-compat", "bye", Token));
            Assert.Equal(1, await notes.DeleteAsync(project, "n-compat", Token));
            await project.CommitAsync(Token);
        }

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.Equal(1, await widgets.DeleteAsync(unit, "w-compat-2", Token));
            await unit.CommitAsync(Token);
        }

        await using var operatorUnit = await store.OpenAsync(UnitOfWorkScope.Operator);
        await SettingRepository.PutAsync(operatorUnit, "compat", "a", Token);
        Assert.Equal("a", await SettingRepository.GetAsync(operatorUnit, "compat", Token));
        await operatorUnit.CommitAsync(Token);
    }
}

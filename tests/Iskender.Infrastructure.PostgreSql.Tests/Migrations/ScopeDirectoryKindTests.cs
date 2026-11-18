using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The scope directory table kind in the runner's policy check (ISK-0009 T008, research R2, persistence contract Isolation
/// rules 7 and 9): a table with <c>listed_scope</c> is applied only with exactly the four permissive per-command policies,
/// and any missing, extra, or differing policy is refused with <c>missing_row_security</c> and nothing applied.
/// </summary>
public class ScopeDirectoryKindTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADirectoryTableWithExactlyTheFourPoliciesIsApplied()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        await store.Runner(new MigrationSet([FixtureModules.Listed])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history WHERE module = 'listed'"));
        Assert.Equal(
            PostgreSqlMigrationRunner.DirectoryReadExpression,
            await fixture.ScalarAsync<string>(database.Name, "SELECT regexp_replace(pg_get_expr(polqual, polrelid), '\\s+', ' ', 'g') FROM pg_policy WHERE polname = 'entries_read'"));
        Assert.Equal(
            PostgreSqlMigrationRunner.DirectoryOwnExpression,
            await fixture.ScalarAsync<string>(database.Name, "SELECT regexp_replace(pg_get_expr(polwithcheck, polrelid), '\\s+', ' ', 'g') FROM pg_policy WHERE polname = 'entries_insert'"));
    }

    public static TheoryData<string> Unprotected() =>
    [
        nameof(FixtureModules.ListedMissingPolicy),
        nameof(FixtureModules.ListedExtraPolicy),
        nameof(FixtureModules.ListedWrongCommand),
        nameof(FixtureModules.ListedWidenedWrite),
    ];

    [Theory]
    [MemberData(nameof(Unprotected))]
    public async Task ADirectoryTableWithoutExactlyTheFourPoliciesIsRefusedAndNothingIsApplied(string module)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var declared = (MigrationModule)typeof(FixtureModules).GetProperty(module)!.GetValue(null)!;

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([declared])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "reason" && value.Value == MigrationRefusal.MissingRowSecurity);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "module" && value.Value == declared.Name);
        Assert.Equal(0L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history"));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, $"SELECT to_regnamespace('isk_{declared.Name}') IS NULL"));
    }

    [Fact]
    public async Task TheExistingKindsAreStillCheckedAlongsideADirectoryTable()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        await store.Runner(new MigrationSet([FixtureModules.Listed, FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([FixtureModules.Listed, FixtureModules.Scoped, FixtureModules.ScopeKeyExtraPolicy])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "module" && value.Value == "scope_key_extra_policy");
        Assert.Equal(2L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history"));
    }

    [Fact]
    public async Task TheListedPlaceholderBindsTheUnitsOwnScopeKey()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Listed])).ApplyAsync(new MigrationApplyRequest(), Token);
        var table = new ScopeDirectoryTable("listed", "entries");
        var organization = Owners.Organization(Owners.OrgA);

        await using (var unit = await store.OpenAsync(organization))
        {
            var adapter = PostgreSqlUnitOfWork.From(unit);
            Assert.Equal(1, await adapter.Command(table, "INSERT INTO isk_listed.entries (listed_scope, module) VALUES ($1, 'work')").ExecuteAsync(Token));
            Assert.Equal(1L, await adapter.Command(table, "SELECT count(*) FROM isk_listed.entries WHERE {listed}").ScalarAsync<long>(Token));
            await unit.CommitAsync(Token);
        }

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            var adapter = PostgreSqlUnitOfWork.From(unit);
            Assert.Equal(0L, await adapter.Command(table, "SELECT count(*) FROM isk_listed.entries WHERE {listed}").ScalarAsync<long>(Token));
            Assert.Equal(1L, await adapter.Command(table, "SELECT count(*) FROM isk_listed.entries").ScalarAsync<long>(Token));
            Assert.Equal(0, await adapter.Command(table, "DELETE FROM isk_listed.entries").ExecuteAsync(Token));
        }
    }
}

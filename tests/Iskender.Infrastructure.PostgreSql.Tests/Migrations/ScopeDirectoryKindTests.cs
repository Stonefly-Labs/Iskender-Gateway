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
            Assert.Equal(1L, await adapter.Command(table, "SELECT count(*) FROM isk_listed.entries WHERE {any_listed} AND module = $1").With("work").ScalarAsync<long>(Token));
        }
    }

    [Theory]
    [InlineData("SELECT count(*) FROM isk_listed.entries")]
    [InlineData("DELETE FROM isk_listed.entries")]
    [InlineData("UPDATE isk_listed.entries SET due_at = NULL")]
    public async Task AStatementWithNoListedPredicateIsRefusedBeforeItIsSent(string sql)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Listed])).ApplyAsync(new MigrationApplyRequest(), Token);

        foreach (var scope in new[] { UnitOfWorkScope.Operator, Owners.Organization(Owners.OrgA) })
        {
            await using var unit = await store.OpenAsync(scope);
            var adapter = PostgreSqlUnitOfWork.From(unit);

            var failure = Assert.Throws<GatewayException>(() => adapter.Command(new ScopeDirectoryTable("listed", "entries"), sql));

            Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
            Assert.Equal(0, adapter.StatementsSent);
        }
    }

    [Theory]
    [InlineData("DELETE FROM isk_listed.entries WHERE {any_listed}")]
    [InlineData("UPDATE isk_listed.entries SET due_at = NULL WHERE {any_listed}")]
    [InlineData("SELECT 1 FROM isk_listed.entries WHERE {any_listed} FOR UPDATE")]
    [InlineData("SELECT 1 FROM isk_listed.entries WHERE {any_listed} FOR KEY SHARE")]
    [InlineData("SELECT 1 FROM isk_listed.entries WHERE {any_listed} OR {listed}")]
    public async Task TheOperatorReadOfEveryRowIsOnlyAPlainSelect(string sql)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Listed])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var adapter = PostgreSqlUnitOfWork.From(unit);

        var failure = Assert.Throws<GatewayException>(() => adapter.Command(new ScopeDirectoryTable("listed", "entries"), sql));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(0, adapter.StatementsSent);
    }

    [Fact]
    public async Task TheOperatorReadOfEveryRowIsRefusedInATenantScope()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Listed])).ApplyAsync(new MigrationApplyRequest(), Token);

        foreach (var scope in new[] { Owners.Organization(Owners.OrgA), Owners.Project(Owners.OrgA, Owners.ProjectA1) })
        {
            await using var unit = await store.OpenAsync(scope);
            var adapter = PostgreSqlUnitOfWork.From(unit);

            var failure = Assert.Throws<GatewayException>(() => adapter.Command(new ScopeDirectoryTable("listed", "entries"), "SELECT count(*) FROM isk_listed.entries WHERE {any_listed}"));

            Assert.Same(ErrorCodes.PersistenceScopeNotPermitted, failure.Code);
            Assert.Equal(UnitOfWorkState.Faulted, unit.State);
            Assert.Equal(0, adapter.StatementsSent);
        }
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Scoped;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Isolation;

/// <summary>
/// Persistence contract version 2, Isolation rules 6 to 8 (ISK-0005 research R6 and R7): a scope-keyed table is usable
/// by every scope, and each scope sees only rows of its exact scope, with the scope predicate and with row-level security
/// alone. The migration runner refuses a scope-keyed table without exactly the scope-keyed policy.
/// </summary>
public class ScopeKeyedTableTests(PostgreSqlFixture fixture)
{
    private static readonly ResourceId ProjectB1 = ResourceId.New(ResourceTypes.Project);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<bool> ScopePredicate() => [true, false];

    // The operator, two organization, and two project scopes, one project in each organization.
    private static (string Name, UnitOfWorkScope Scope)[] Scopes() =>
    [
        ("operator", UnitOfWorkScope.Operator),
        ("org a", Owners.Organization(Owners.OrgA)),
        ("org b", Owners.Organization(Owners.OrgB)),
        ("project a1", Owners.Project(Owners.OrgA, Owners.ProjectA1)),
        ("project b1", Owners.Project(Owners.OrgB, ProjectB1)),
    ];

    [Theory]
    [MemberData(nameof(ScopePredicate))]
    public async Task EachScopeSeesAndChangesOnlyItsOwnRows(bool scopePredicate)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);
        var scopes = Scopes();

        for (var index = 0; index < scopes.Length; index++)
        {
            await using var unit = await store.OpenAsync(scopes[index].Scope);
            var adapter = PostgreSqlUnitOfWork.From(unit);
            adapter.OwnerPredicate = scopePredicate;
            Assert.Equal(1, await adapter.Command(ScopedTables.Slots, "INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ($1, $2, $3)")
                .With("slot").With((long)index).ExecuteAsync(Token));
            await unit.CommitAsync(Token);
        }

        Assert.Equal(5L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_scoped.slots"));

        for (var index = 0; index < scopes.Length; index++)
        {
            await using var unit = await store.OpenAsync(scopes[index].Scope);
            var adapter = PostgreSqlUnitOfWork.From(unit);
            adapter.OwnerPredicate = scopePredicate;

            var visible = await adapter.Command(ScopedTables.Slots, "SELECT value FROM isk_scoped.slots WHERE {scope} ORDER BY value")
                .QueryAsync(row => row.GetInt64(0), Token);
            var updated = await adapter.Command(ScopedTables.Slots, "UPDATE isk_scoped.slots SET value = value + 100 WHERE {scope}").ExecuteAsync(Token);

            Assert.Equal(new[] { (long)index }, visible);
            Assert.Equal(1, updated);
            await unit.CommitAsync(Token);
        }

        Assert.Equal(new long[] { 100, 101, 102, 103, 104 }, await ValuesAsync(database));
    }

    [Theory]
    [MemberData(nameof(ScopePredicate))]
    public async Task AProjectSeesNoneOfItsOrganizationsRowsAndTheReverse(bool scopePredicate)
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);
        await InsertAsync(store, Owners.Organization(Owners.OrgA), "org", 1);
        await InsertAsync(store, Owners.Project(Owners.OrgA, Owners.ProjectA1), "project", 2);

        Assert.Equal(["org"], await NamesAsync(store, Owners.Organization(Owners.OrgA), scopePredicate));
        Assert.Equal(["project"], await NamesAsync(store, Owners.Project(Owners.OrgA, Owners.ProjectA1), scopePredicate));
        Assert.Empty(await NamesAsync(store, Owners.Project(Owners.OrgA, Owners.ProjectA2), scopePredicate));
        Assert.Empty(await NamesAsync(store, UnitOfWorkScope.Operator, scopePredicate));
    }

    [Fact]
    public async Task AUnitCannotWriteARowKeyedToAnotherScope()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            var adapter = PostgreSqlUnitOfWork.From(unit);

            await Assert.ThrowsAsync<GatewayException>(async () =>
                await adapter.FrameworkCommand("INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ('operator', 'forged', 1)").ExecuteAsync(Token));
            Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        }

        Assert.Equal(0L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_scoped.slots"));
    }

    [Fact]
    public async Task WithNoTransactionLocalSettingsNoRowMatches()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);
        foreach (var (name, scope) in Scopes())
        {
            await InsertAsync(store, scope, name.Replace(' ', '_'), 1);
        }

        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(Token);
        await using var read = new NpgsqlCommand("SELECT count(*) FROM isk_scoped.slots", connection);
        await using var change = new NpgsqlCommand("UPDATE isk_scoped.slots SET value = 0", connection);

        Assert.Equal(0L, (long)(await read.ExecuteScalarAsync(Token))!);
        Assert.Equal(0, await change.ExecuteNonQueryAsync(Token));
        Assert.Equal(5L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_scoped.slots WHERE value = 1"));
    }

    [Fact]
    public async Task AStatementThatNamesNeitherTheScopeNorItsKeyIsRefusedBeforeItIsSent()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var adapter = PostgreSqlUnitOfWork.From(unit);

        var failure = Assert.Throws<GatewayException>(() => adapter.Command(ScopedTables.Slots, "SELECT value FROM isk_scoped.slots"));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(0, adapter.StatementsSent);
    }

    public static TheoryData<string> Unprotected() =>
    [
        nameof(FixtureModules.ScopeKeyNoPolicy),
        nameof(FixtureModules.ScopeKeyWrongPolicy),
        nameof(FixtureModules.ScopeKeyExtraPolicy),
    ];

    [Theory]
    [MemberData(nameof(Unprotected))]
    public async Task AScopeKeyedTableWithoutExactlyThePolicyIsRefusedAndNothingIsApplied(string module)
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
    public async Task TheScopeKeyedPolicyIsAcceptedAndMatchesTheRunnersExpression()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        await store.Runner(new MigrationSet([FixtureModules.Scoped])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history WHERE module = 'scoped'"));
        Assert.Equal(
            PostgreSqlMigrationRunner.ScopeKeyedPolicyExpression,
            await fixture.ScalarAsync<string>(database.Name, "SELECT regexp_replace(pg_get_expr(polqual, polrelid), '\\s+', ' ', 'g') FROM pg_policy WHERE polname = 'slots_scope'"));
    }

    private static async Task InsertAsync(TestStore store, UnitOfWorkScope scope, string name, long value)
    {
        await using var unit = await store.OpenAsync(scope);
        await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, "INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ($1, $2, $3)")
            .With(name).With(value).ExecuteAsync(Token);
        await unit.CommitAsync(Token);
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(TestStore store, UnitOfWorkScope scope, bool scopePredicate)
    {
        await using var unit = await store.OpenAsync(scope);
        var adapter = PostgreSqlUnitOfWork.From(unit);
        adapter.OwnerPredicate = scopePredicate;
        return await adapter.Command(ScopedTables.Slots, "SELECT name FROM isk_scoped.slots WHERE {scope} ORDER BY name").QueryAsync(row => row.GetString(0), Token);
    }

    private static async Task<long[]> ValuesAsync(ClassDatabase database)
    {
        await using var connection = await database.Fixture.OpenSuperuserAsync(database.Name);
        await using var command = new NpgsqlCommand("SELECT value FROM isk_scoped.slots ORDER BY value", connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var values = new List<long>();
        while (await reader.ReadAsync(Token))
        {
            values.Add(reader.GetInt64(0));
        }

        return [.. values];
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The <c>directory</c> migration module (ISK-0009 T009, FR-006, research R2): it is <c>directory</c> 1 to 1 with no
/// dependencies; it applies, is accepted by the runner's scope directory check, reverses only with
/// <c>ConfirmDataLoss</c>, and applies again; and its one table has exactly the shape and policies of persistence contract
/// Isolation rule 9.
/// </summary>
public class DirectoryMigrationTests(PostgreSqlFixture fixture)
{
    private static readonly string Own = Literal(PostgreSqlMigrationRunner.DirectoryOwnExpression);

    private static readonly string Read = Literal(PostgreSqlMigrationRunner.DirectoryReadExpression);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheModuleIsDirectoryOneToOneWithNoDependenciesAndItsScriptsShipInTheAdapter()
    {
        var module = DirectoryMigrations.Module;
        var set = new MigrationSet([module]);

        Assert.Equal(("directory", "isk_directory", new SchemaVersionRange(1, 1)), (module.Name, module.Schema, module.SupportedRange));
        Assert.Empty(module.DependsOn);
        var script = Assert.Single(set.Migrations);
        Assert.Equal(new MigrationIdentity("directory", 1), script.Identity);
        Assert.True(script.DataLosing);
    }

    [Fact]
    public async Task ItAppliesReversesWithConfirmationAndAppliesAgain()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(new MigrationSet([DirectoryMigrations.Module]));

        Assert.Equal([new MigrationIdentity("directory", 1)], (await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
        var applied = await CatalogSnapshot.RenderAsync(fixture, database.Name);
        Assert.Contains("scopes", applied, StringComparison.Ordinal);

        var unconfirmed = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, unconfirmed.Code);

        Assert.Equal([new MigrationIdentity("directory", 1)], (await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token)).Reversed);
        Assert.Equal("", await CatalogSnapshot.RenderAsync(fixture, database.Name));

        Assert.Equal([new MigrationIdentity("directory", 1)], (await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
        Assert.Equal(applied, await CatalogSnapshot.RenderAsync(fixture, database.Name));
    }

    [Fact]
    public async Task TheTableHasExactlyTheDirectoryShapeAndFourPolicies()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(
            "listed_scope text not null|module text not null|due_at timestamp with time zone null",
            await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(attname || ' ' || format_type(atttypid, atttypmod) || CASE WHEN attnotnull THEN ' not null' ELSE ' null' END, '|' ORDER BY attnum) FROM pg_attribute WHERE attrelid = 'isk_directory.scopes'::regclass AND attnum > 0 AND NOT attisdropped"));
        Assert.Equal(
            "listed_scope,module",
            await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(a.attname, ',' ORDER BY k.ord) FROM pg_index i CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ord) JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum WHERE i.indrelid = 'isk_directory.scopes'::regclass AND i.indisprimary"));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'isk_directory.scopes'::regclass"));
        Assert.Equal(
            "scopes_delete|d|own|-,scopes_insert|a|-|own,scopes_read|r|read|-,scopes_update|w|own|own",
            await fixture.ScalarAsync<string>(
                database.Name,
                "SELECT string_agg(polname || '|' || polcmd::text || '|' || " +
                "CASE regexp_replace(coalesce(pg_get_expr(polqual, polrelid), '-'), '\\s+', ' ', 'g') WHEN '-' THEN '-' WHEN " + Own + " THEN 'own' WHEN " + Read + " THEN 'read' ELSE 'other' END || '|' || " +
                "CASE regexp_replace(coalesce(pg_get_expr(polwithcheck, polrelid), '-'), '\\s+', ' ', 'g') WHEN '-' THEN '-' WHEN " + Own + " THEN 'own' ELSE 'other' END, ',' ORDER BY polname) " +
                "FROM pg_policy WHERE polrelid = 'isk_directory.scopes'::regclass AND polpermissive"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_directory' AND indexname = 'scopes_due' AND indexdef LIKE '%(module, listed_scope) WHERE (due_at IS NOT NULL)%'"));
    }

    [Fact]
    public async Task TheModuleColumnRejectsANameOutsideTheModulePattern()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await PostgreSqlUnitOfWork.From(unit).Command(DirectoryTables.Scopes, "INSERT INTO isk_directory.scopes (listed_scope, module) VALUES ($1, 'Bad-Module')").ExecuteAsync(Token));

        Assert.NotSame(ErrorCodes.PersistenceScopeNotPermitted, failure.Code);
    }

    private static string Literal(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
}

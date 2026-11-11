using System.Runtime.CompilerServices;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The <c>coordination</c> migration module (ISK-0005 T013, FR-024, SC-008): it applies, reverses only with
/// <c>ConfirmDataLoss</c>, and applies again with the catalog matching the committed snapshot; every table is scope-keyed
/// with exactly the scope-keyed policy; the runtime login can use the sequence and DML but changes no object; and nothing
/// is created outside <c>isk_coordination</c>. Set <c>ISKENDER_WRITE_SNAPSHOTS=1</c> to rewrite the snapshot; the test
/// then fails, so a rewrite is never a pass.
/// </summary>
public class CoordinationMigrationTests(PostgreSqlFixture fixture)
{
    private const string WriteSwitch = "ISKENDER_WRITE_SNAPSHOTS";
    private const string SnapshotName = "coordination_0001.txt";

    private static readonly string[] Tables = ["cache_generations", "coordination_values", "fences", "leases"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheModuleIsCoordinationOneToOneWithNoDependenciesAndItsScriptsShipInTheAdapter()
    {
        var module = CoordinationMigrations.Module;
        var set = new MigrationSet([module]);

        Assert.Equal("coordination", module.Name);
        Assert.Equal("isk_coordination", module.Schema);
        Assert.Equal(new SchemaVersionRange(1, 1), module.SupportedRange);
        Assert.Empty(module.DependsOn);
        var script = Assert.Single(set.Migrations);
        Assert.Equal(new MigrationIdentity("coordination", 1), script.Identity);
        Assert.True(script.DataLosing);
    }

    [Fact]
    public async Task ItAppliesReversesWithConfirmationAndAppliesAgainMatchingTheSnapshot()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(new MigrationSet([CoordinationMigrations.Module]));
        var writing = Environment.GetEnvironmentVariable(WriteSwitch) == "1";
        Assert.Equal("", await CatalogSnapshot.RenderAsync(fixture, database.Name));

        Assert.Equal([new MigrationIdentity("coordination", 1)], (await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
        var applied = await CatalogSnapshot.RenderAsync(fixture, database.Name);
        if (writing)
        {
            await File.WriteAllTextAsync(Path.Combine(SourceSnapshots(), SnapshotName), applied, Token);
        }
        else
        {
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", SnapshotName), Token), applied);
        }

        Assert.False(writing, $"The snapshot was written to {SourceSnapshots()}. Review it, then run again without {WriteSwitch}.");

        var unconfirmed = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, unconfirmed.Code);
        Assert.Equal(applied, await CatalogSnapshot.RenderAsync(fixture, database.Name));

        Assert.Equal([new MigrationIdentity("coordination", 1)], (await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token)).Reversed);
        Assert.Equal("", await CatalogSnapshot.RenderAsync(fixture, database.Name));
        Assert.Equal(0L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history"));

        Assert.Equal([new MigrationIdentity("coordination", 1)], (await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
        Assert.Equal(applied, await CatalogSnapshot.RenderAsync(fixture, database.Name));
    }

    [Fact]
    public async Task EveryTableIsScopeKeyedWithEveryColumnNotNullAndExactlyTheScopeKeyedPolicy()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(Tables, await StringsAsync(database, "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'isk_coordination' AND c.relkind = 'r' ORDER BY c.relname COLLATE \"C\""));
        foreach (var table in Tables)
        {
            Assert.True(await fixture.ScalarAsync<bool>(database.Name, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'isk_coordination.{table}'::regclass"));
            Assert.True(await fixture.ScalarAsync<bool>(database.Name, $"SELECT bool_and(attnotnull) FROM pg_attribute WHERE attrelid = 'isk_coordination.{table}'::regclass AND attnum > 0 AND NOT attisdropped"));
            Assert.True(await fixture.ScalarAsync<bool>(database.Name, $"SELECT EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = 'isk_coordination.{table}'::regclass AND attname = 'scope_key' AND atttypid = 'text'::regtype)"));
            Assert.Equal(
                [PostgreSqlMigrationRunner.ScopeKeyedPolicyExpression + "|" + PostgreSqlMigrationRunner.ScopeKeyedPolicyExpression + "|*|True"],
                await StringsAsync(database, $"SELECT regexp_replace(pg_get_expr(polqual, polrelid), '\\s+', ' ', 'g') || '|' || regexp_replace(pg_get_expr(polwithcheck, polrelid), '\\s+', ' ', 'g') || '|' || polcmd::text || '|' || CASE WHEN polpermissive THEN 'True' ELSE 'False' END FROM pg_policy WHERE polrelid = 'isk_coordination.{table}'::regclass"));
        }
    }

    [Fact]
    public async Task TheRuntimeLoginCanUseTheSequenceAndRowsButCannotCreateAlterOrDropAnything()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            var adapter = PostgreSqlUnitOfWork.From(unit);
            var first = await adapter.FrameworkCommand("SELECT nextval('isk_coordination.fencing_tokens')").ScalarAsync<long>(Token);
            var second = await adapter.FrameworkCommand("SELECT nextval('isk_coordination.fencing_tokens')").ScalarAsync<long>(Token);
            Assert.True(second > first);
            Assert.Equal(1, await adapter.Command(CoordinationTables.Values, "INSERT INTO isk_coordination.coordination_values (scope_key, key_hash, value, version, expires_at) VALUES ($1, 'h', 1, 1, now() + interval '1 minute')").ExecuteAsync(Token));
            Assert.Equal(1, await adapter.Command(CoordinationTables.Values, "UPDATE isk_coordination.coordination_values SET value = 2 WHERE {scope} AND key_hash = 'h'").ExecuteAsync(Token));
            Assert.Equal(2L, await adapter.Command(CoordinationTables.Values, "SELECT value FROM isk_coordination.coordination_values WHERE {scope} AND key_hash = 'h'").ScalarAsync<long>(Token));
            Assert.Equal(1, await adapter.Command(CoordinationTables.Values, "DELETE FROM isk_coordination.coordination_values WHERE {scope}").ExecuteAsync(Token));
            await unit.CommitAsync(Token);
        }

        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(Token);
        foreach (var statement in new[]
        {
            "CREATE TABLE isk_coordination.extra (a int)",
            "CREATE SEQUENCE isk_coordination.extra_tokens",
            "ALTER TABLE isk_coordination.leases ADD COLUMN extra int",
            "ALTER TABLE isk_coordination.fences DISABLE ROW LEVEL SECURITY",
            "DROP POLICY leases_scope ON isk_coordination.leases",
            "ALTER SEQUENCE isk_coordination.fencing_tokens RESTART WITH 1",
            "DROP TABLE isk_coordination.cache_generations",
            "DROP SEQUENCE isk_coordination.fencing_tokens",
            "TRUNCATE isk_coordination.leases",
        })
        {
            await using var command = new NpgsqlCommand(statement, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Token));
            Assert.True(failure.SqlState is PostgresErrorCodes.InsufficientPrivilege, $"{statement}: {failure.SqlState}");
        }
    }

    [Fact]
    public async Task NothingIsCreatedOutsideIskCoordination()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        const string Outside = "n.nspname NOT IN ('isk_coordination', 'pg_catalog', 'information_schema', 'pg_toast') AND n.nspname NOT LIKE 'pg\\_temp\\_%' AND n.nspname NOT LIKE 'pg\\_toast\\_temp\\_%'";
        string[] objects =
        [
            $"SELECT 'class ' || n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE {Outside}",
            $"SELECT 'proc ' || n.nspname || '.' || p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE {Outside}",
            $"SELECT 'type ' || n.nspname || '.' || t.typname FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE {Outside}",
            "SELECT 'schema ' || nspname FROM pg_namespace WHERE nspname <> 'isk_coordination'",
            "SELECT 'role ' || rolname FROM pg_roles",
        ];
        var query = string.Join(" UNION ALL ", objects) + " ORDER BY 1";

        // The history table and its schema exist before the module is applied, so the first run's own objects are excluded.
        await store.Runner(new MigrationSet([])).ApplyAsync(new MigrationApplyRequest(), Token);
        var before = await StringsAsync(database, query);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(before, await StringsAsync(database, query));
        Assert.Equal(["fencing_tokens"], await StringsAsync(database, "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'isk_coordination' AND c.relkind = 'S'"));
    }

    private async Task<string[]> StringsAsync(ClassDatabase database, string sql)
    {
        await using var connection = await fixture.OpenSuperuserAsync(database.Name);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var values = new List<string>();
        while (await reader.ReadAsync(Token))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static string SourceSnapshots([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path))!, "Snapshots");
}

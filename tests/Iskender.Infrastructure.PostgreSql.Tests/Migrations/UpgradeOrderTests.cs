using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The upgrade from the previous release (ISK-0009 T039, plan Risks, persistence contract Migrations rule 12): a database
/// with only <c>coordination</c> and its rows reports <c>schema_behind</c> to this release; applying this release adds
/// <c>directory</c> and <c>work</c> and leaves <c>coordination</c>'s history and rows unchanged; reversing two steps with
/// data-loss confirmation removes <c>work</c> and <c>directory</c> and leaves <c>coordination</c> and its rows.
/// </summary>
public class UpgradeOrderTests(PostgreSqlFixture fixture)
{
    // The rows the previous release's coordinator wrote: an atomic value, a lease, a fence, and a generation.
    private const string PreviousRowsSql = """
        INSERT INTO isk_coordination.coordination_values (scope_key, key_hash, value, version, expires_at) VALUES ('operator', repeat('a', 64), 4, 2, clock_timestamp() + interval '1 hour');
        INSERT INTO isk_coordination.leases (scope_key, key_hash, holder, token, expires_at) VALUES ('operator', repeat('b', 64), gen_random_uuid(), nextval('isk_coordination.fencing_tokens'), clock_timestamp() + interval '1 hour');
        INSERT INTO isk_coordination.fences (scope_key, resource_hash, lease_hash, highest_token) VALUES ('operator', repeat('c', 64), repeat('b', 64), 1);
        INSERT INTO isk_coordination.cache_generations (scope_key, namespace_hash, generation) VALUES ('operator', repeat('d', 64), 3);
        """;

    private const string HistorySql =
        "SELECT string_agg(module || '/' || version || '/' || checksum || '/' || applied_at::text, ',' ORDER BY module, version) FROM isk_migrations.history WHERE module = 'coordination'";

    private const string RowsSql =
        "SELECT (SELECT string_agg(c::text, ',' ORDER BY key_hash) FROM isk_coordination.coordination_values c) || '|' "
        + "|| (SELECT string_agg(l::text, ',' ORDER BY key_hash) FROM isk_coordination.leases l) || '|' "
        + "|| (SELECT string_agg(f::text, ',' ORDER BY resource_hash) FROM isk_coordination.fences f) || '|' "
        + "|| (SELECT string_agg(g::text, ',' ORDER BY namespace_hash) FROM isk_coordination.cache_generations g)";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheUpgradeAddsDirectoryAndWorkAndTheReverseRemovesThemLeavingCoordinationAndItsRows()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([CoordinationMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);
        await fixture.ExecuteAsync(database.Name, PreviousRowsSql);
        var history = await fixture.ScalarAsync<string>(database.Name, HistorySql);
        var rows = await fixture.ScalarAsync<string>(database.Name, RowsSql);

        var release = new MigrationSet([CoordinationMigrations.Module, DirectoryMigrations.Module, WorkMigrations.Module]);
        var probe = new PostgreSqlHealthProbe(store.Sources, release, TimeProvider.System, store.Logger);
        Assert.Equal(PersistenceHealthReason.SchemaBehind, (await probe.CheckAsync(Token)).Reason);

        var runner = store.Runner(release);
        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal("coordination/1,directory/1,work/1", await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(module || '/' || version, ',' ORDER BY module) FROM isk_migrations.history"));
        Assert.Equal(history, await fixture.ScalarAsync<string>(database.Name, HistorySql));
        Assert.Equal(rows, await fixture.ScalarAsync<string>(database.Name, RowsSql));
        Assert.Equal(PersistenceHealthReason.Ok, (await probe.CheckAsync(Token)).Reason);

        var reversed = await runner.ReverseAsync(new MigrationReverseRequest { Steps = 2, ConfirmDataLoss = true }, Token);

        Assert.Equal([new MigrationIdentity("work", 1), new MigrationIdentity("directory", 1)], reversed.Reversed);
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_work.items') IS NULL AND to_regclass('isk_directory.scopes') IS NULL"));
        Assert.Equal(history, await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(module || '/' || version || '/' || checksum || '/' || applied_at::text, ',' ORDER BY module, version) FROM isk_migrations.history"));
        Assert.Equal(rows, await fixture.ScalarAsync<string>(database.Name, RowsSql));
    }
}

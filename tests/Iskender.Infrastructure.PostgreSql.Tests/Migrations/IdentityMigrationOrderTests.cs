using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The release order with ISK-0010's modules (ISK-0010 T016, FR-032, persistence contract Migrations rule 12): forward
/// is <c>coordination</c>, <c>directory</c>, <c>work</c>, <c>audit</c>, <c>identity</c>; upgrading a database with
/// ISK-0009's modules adds exactly <c>audit</c> and <c>identity</c>; and reversing two steps with confirmation removes
/// <c>identity</c>, then <c>audit</c>, leaving every ISK-0009 module and its rows untouched.
/// </summary>
public class IdentityMigrationOrderTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly MigrationModule[] Isk0009 = [CoordinationMigrations.Module, DirectoryMigrations.Module, WorkMigrations.Module];

    private static MigrationSet Release => new([.. Isk0009, AuditMigrations.Module, IdentityMigrations.Module]);

    private const string Isk0009Rows =
        "SELECT (SELECT count(*) FROM isk_work.items) || '|' || (SELECT count(*) FROM isk_directory.scopes) || '|' || (SELECT count(*) FROM isk_coordination.coordination_values)";

    [Fact]
    public void TheForwardOrderEndsWithAuditThenIdentity()
    {
        Assert.Equal(["coordination/1", "directory/1", "work/1", "audit/1", "identity/1"], Release.Migrations.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public async Task UpgradingAddsExactlyAuditAndIdentityAndReversingTwoStepsLeavesIsk0009Untouched()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet(Isk0009)).ApplyAsync(new MigrationApplyRequest(), Token);
        await fixture.ExecuteAsync(database.Name, Seed);
        var before = await History(database);
        var rows = await fixture.ScalarAsync<string>(database.Name, Isk0009Rows);

        var applied = await store.Runner(Release).ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal([new MigrationIdentity("audit", 1), new MigrationIdentity("identity", 1)], applied.Applied);
        Assert.Equal(before + ",audit/1,identity/1", await History(database));

        var reversed = await store.Runner(Release).ReverseAsync(new MigrationReverseRequest { Steps = 2, ConfirmDataLoss = true }, Token);

        Assert.Equal([new MigrationIdentity("identity", 1), new MigrationIdentity("audit", 1)], reversed.Reversed);
        Assert.Equal(before, await History(database));
        Assert.Equal(rows, await fixture.ScalarAsync<string>(database.Name, Isk0009Rows));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('isk_identity', 'isk_audit') AND c.relkind = 'r')"));
    }

    private Task<string> History(ClassDatabase database) => fixture.ScalarAsync<string>(
        database.Name, "SELECT string_agg(module || '/' || version, ',' ORDER BY applied_at, module) FROM isk_migrations.history");

    // One row in each ISK-0009 table the reverse must leave alone.
    private const string Seed =
        "INSERT INTO isk_coordination.coordination_values (scope_key, key_hash, value, version, expires_at) VALUES ('operator', 'k', 1, 1, now() + interval '1 hour');";
}

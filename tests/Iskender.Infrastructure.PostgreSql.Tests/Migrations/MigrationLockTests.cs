using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

public class MigrationLockTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private const string LockRows =
        "SELECT count(*) FROM pg_locks l JOIN pg_database d ON d.oid = l.database " +
        "WHERE l.locktype = 'advisory' AND l.classid = 1433339375 AND l.objid = 1001676181 AND l.objsubid = 1 AND d.datname = current_database()";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ThePublishedKeyIsTheOneInPgLocks()
    {
        Assert.Equal(6156145740695756181, MigrationLock.Key);
        Assert.Equal(0x556F05EF3BB45D95, MigrationLock.Key);
        Assert.Equal(1433339375u, (uint)(MigrationLock.Key >> 32));
        Assert.Equal(1001676181u, unchecked((uint)MigrationLock.Key));
    }

    [Fact]
    public async Task TheLockIsHeldDuringARunAndReleasedAfter()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var runner = store.Runner(FixtureModules.Release1);
        long during = -1;
        runner.WhileLocked = async _ => during = await database.Fixture.ScalarAsync<long>(database.Name, LockRows);

        var result = await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.Equal(6, result.Applied.Count);
        Assert.Equal(1, during);
        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, LockRows));
        Assert.True(await database.Fixture.ScalarAsync<bool>(database.Name, "SELECT has_table_privilege('iskender_runtime', 'isk_migrations.history', 'SELECT')"));
        Assert.Equal([4007, 4009, 4009, 4009, 4009, 4009, 4009, 4011], store.Logger.Events.Select(logged => logged.Id.Id));
    }

    [Fact]
    public async Task AHeldLockMakesASecondRunnerFailAfterItsWaitHavingChangedNothing()
    {
        database.RequirePostgreSql();
        await using var scratch = await ScratchDatabase.CreateAsync(database.Fixture);
        var name = scratch.Name;
        await using var holder = await database.Fixture.OpenSuperuserAsync(name);
        await using (var take = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key})", holder))
        {
            await take.ExecuteScalarAsync(Token);
        }

        await using var store = TestStore.For(scratch, TestSettings.MigrationWithLockWait("1s"));
        var clock = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceMigrationLocked, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        Assert.True(await database.Fixture.ScalarAsync<bool>(name, "SELECT to_regnamespace('isk_migrations') IS NULL"));
        Assert.Equal([4007, 4008, 4011], store.Logger.Events.Select(logged => logged.Id.Id));
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4011 && logged.Fields.Any(field => field.Key == "Outcome" && (string?)field.Value == "locked"));
    }

    [Fact]
    public async Task AMissingGroupRoleIsRefusedBeforeTheLock()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var runner = store.Runner(FixtureModules.Release1);
        runner.RuntimeRole = "isk_no_such_role";
        var locked = false;
        runner.WhileLocked = _ => Task.FromResult(locked = true);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "reason" && value.Value == MigrationRefusal.MissingRuntimeRole);
        Assert.False(locked);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4012);
    }

    [Fact]
    public async Task MissingMigrationCredentialsFailBeforeConnecting()
    {
        database.RequirePostgreSql();
        var credentials = TestCredentialProvider.For(database.Fixture);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database, withMigration: false), credentials);
        var runner = new PostgreSqlMigrationRunner(sources, FixtureModules.Release1);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
        Assert.Empty(credentials.Requests);
    }
}

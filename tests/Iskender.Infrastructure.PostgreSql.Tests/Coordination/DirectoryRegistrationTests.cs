using System.Reflection;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using ConformanceScopes = Iskender.Coordination.Conformance.ConformanceScopes;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The coordinator's scope directory registration (ISK-0009 T039, coordination contract Atomic operations rule 7 and
/// Known limits): a write that inserts a new expiring row registers its scope under module <c>coordination</c> with the
/// row's expiry as the due time, in the same unit; an update, a fence record, and a namespace generation register
/// nothing; a row from before the upgrade gets its scope registered only when the scope next inserts a row. Each test uses
/// scopes of its own, because the directory row is the scope's, not the key's.
/// </summary>
public class DirectoryRegistrationTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachWriteThatInsertsANewRowRegistersItsScopeDueAtTheRowsExpiry()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        var atomic = coordinator.CreateClient(0).Atomic;

        await atomic.AddAsync(scope, Name("added"), 1, Long, Token);
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "added"), await DueAsync(scope));

        await ForgetAsync(scope);
        Assert.True((await atomic.ReserveAsync(scope, Name("reserved"), 1, 10, Long, Token)).Granted);
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "reserved"), await DueAsync(scope));

        await ForgetAsync(scope);
        Assert.True(await atomic.CompareAndSetAsync(scope, Name("created"), 0, 7, Long, Token));
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "created"), await DueAsync(scope));

        await ForgetAsync(scope);
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("leased"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        Assert.Equal(await ExpiryAsync("leases", scope, "leased"), await DueAsync(scope));
        await coordinator.Leases(0).ReleaseAsync(lease, Token);

        Assert.Equal(1L, await RowsAsync(scope));
    }

    [Fact]
    public async Task ANewKeyWithAnEarlierExpiryLowersTheDueTimeAndALaterOneLeavesIt()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        var atomic = coordinator.CreateClient(0).Atomic;

        await atomic.AddAsync(scope, Name("later"), 1, Long, Token);
        await atomic.AddAsync(scope, Name("earlier"), 1, TimeSpan.FromMinutes(1), Token);
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "earlier"), await DueAsync(scope));

        await atomic.AddAsync(scope, Name("latest"), 1, TimeSpan.FromMinutes(10), Token);
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "earlier"), await DueAsync(scope));
        Assert.Equal(1L, await RowsAsync(scope));
    }

    [Fact]
    public async Task AWriteThatUpdatesAnExistingRowRegistersNothing()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        var atomic = coordinator.CreateClient(0).Atomic;
        await atomic.AddAsync(scope, Name("value"), 1, Long, Token);
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("lease"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        await atomic.AddAsync(scope, Name("brief"), 1, TimeSpan.FromMilliseconds(300), Token);
        await Task.Delay(TimeSpan.FromMilliseconds(450), Token);
        await ForgetAsync(scope);

        await atomic.AddAsync(scope, Name("value"), 1, Long, Token);
        Assert.True((await atomic.ReserveAsync(scope, Name("value"), 1, 10, Long, Token)).Granted);
        Assert.True(await atomic.CompareAndSetAsync(scope, Name("value"), 3, 9, Long, Token));
        await atomic.ReleaseAsync(scope, Name("value"), 1, Token);
        lease = await coordinator.Leases(0).RenewAsync(lease, TimeSpan.FromMinutes(1), Token);
        await atomic.AddAsync(scope, Name("brief"), 1, Long, Token);

        Assert.Equal(0L, await RowsAsync(scope));
        await coordinator.Leases(0).ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task TheExpiredPlaceholderALeaseReleaseInsertsRegistersItsScope()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        var leases = coordinator.Leases(0);
        var key = CoordinationKey.Hash(scope.Owner, "registration", "placeholder");

        // The release of an acquire whose outcome is unknown runs only after a cut-off commit, so it is called directly.
        var release = typeof(PostgreSqlLeaseCoordinator).GetMethod("ReleaseHolderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (ValueTask)release.Invoke(leases, [scope, "registration", key, Guid.NewGuid()])!;

        Assert.Equal(await ExpiryAsync("leases", scope, "placeholder"), await DueAsync(scope));
    }

    [Fact]
    public async Task FenceRecordsAndNamespaceGenerationsRegisterNothing()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().ProjectA;
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("fenced"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        await ForgetAsync(scope);
        var fences = await CountAsync("fences", scope);
        var generations = await CountAsync("cache_generations", scope);

        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Name("resource"), lease, Token));
        await coordinator.Cache(0).InvalidateAsync(scope, new CacheNamespace("registration", "entries"), Token);

        Assert.Equal(fences + 1, await CountAsync("fences", scope));
        Assert.Equal(generations + 1, await CountAsync("cache_generations", scope));
        Assert.Equal(0L, await RowsAsync(scope));
        await coordinator.Leases(0).ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task ARowFromBeforeTheUpgradeIsRegisteredOnlyWhenItsScopeNextInsertsARow()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        var atomic = coordinator.CreateClient(0).Atomic;

        // Written as the previous release wrote it: the row, and no directory row.
        await coordinator.Fixture.ExecuteAsync(
            coordinator.Database.Name,
            $"INSERT INTO isk_coordination.coordination_values (scope_key, key_hash, value, version, expires_at) VALUES ('{ScopeKey(scope)}', '{CoordinationKey.Hash(scope.Owner, "registration", "old")}', 1, 1, clock_timestamp() + interval '5 minutes')");
        Assert.Equal(0L, await RowsAsync(scope));

        await atomic.AddAsync(scope, Name("old"), 1, Long, Token);
        Assert.Equal(0L, await RowsAsync(scope));

        await atomic.AddAsync(scope, Name("new"), 1, TimeSpan.FromMinutes(10), Token);
        Assert.Equal(await ExpiryAsync("coordination_values", scope, "new"), await DueAsync(scope));
    }

    private static CoordinationName Name(string name) => new("registration", name);

    private static string ScopeKey(CoordinationScope scope) => CoordinationKey.ScopeKey(scope.Owner);

    private Task ForgetAsync(CoordinationScope scope) =>
        coordinator.Fixture.ExecuteAsync(coordinator.Database.Name, $"DELETE FROM isk_directory.scopes WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

    private Task<long> RowsAsync(CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_directory.scopes WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

    private Task<long> CountAsync(string table, CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.{table} WHERE scope_key = '{ScopeKey(scope)}'");

    private Task<DateTime> DueAsync(CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<DateTime>(coordinator.Database.Name, $"SELECT due_at FROM isk_directory.scopes WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

    private Task<DateTime> ExpiryAsync(string table, CoordinationScope scope, string name) =>
        coordinator.Fixture.ScalarAsync<DateTime>(coordinator.Database.Name, $"SELECT expires_at FROM isk_coordination.{table} WHERE scope_key = '{ScopeKey(scope)}' AND key_hash = '{CoordinationKey.Hash(scope.Owner, "registration", name)}'");
}

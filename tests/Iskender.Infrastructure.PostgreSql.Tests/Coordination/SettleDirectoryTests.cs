using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using ConformanceScopes = Iskender.Coordination.Conformance.ConformanceScopes;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// <c>CoordinationMaintenance.SettleDirectoryAsync</c> (ISK-0009 T040, coordination contract Atomic operations rule 8):
/// it removes the scope's <c>coordination</c> row when no atomic-operation or lease row of the scope remains, keeps it with
/// their earliest expiry otherwise, and ignores fence records and namespace generations. Each test uses scopes of its own.
/// </summary>
public class SettleDirectoryTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(300);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationMaintenance Maintenance => new(coordinator.Units(0), coordinator.CoordinationSettings);

    [Fact]
    public async Task ItRemovesTheRowOnceTheScopesExpiredRowsArePurged()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        await coordinator.CreateClient(0).Atomic.AddAsync(scope, Name("brief"), 1, Brief, Token);
        await coordinator.Leases(0).AcquireAsync(scope, Name("brief_lease"), Brief, LeaseWait.TryOnly, Token);
        await coordinator.Fixture.UntilAsync(
            coordinator.Database.Name,
            $"SELECT NOT EXISTS (SELECT 1 FROM isk_coordination.coordination_values WHERE scope_key = '{ScopeKey(scope)}' AND expires_at > clock_timestamp()) "
            + $"AND NOT EXISTS (SELECT 1 FROM isk_coordination.leases WHERE scope_key = '{ScopeKey(scope)}' AND expires_at > clock_timestamp())",
            "the scope's brief rows to expire");

        // An expired row still counts until it is purged, and is due at once.
        await Maintenance.SettleDirectoryAsync(scope, Token);
        Assert.Equal(await EarliestAsync(scope), await DueAsync(scope));

        Assert.Equal(2, await Maintenance.PurgeExpiredAsync(scope, 100, Token));
        await Maintenance.SettleDirectoryAsync(scope, Token);
        Assert.Equal(0L, await RowsAsync(scope));
    }

    [Fact]
    public async Task ItKeepsTheRowWithTheEarliestExpiryAcrossBothTables()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationA;
        await coordinator.CreateClient(0).Atomic.AddAsync(scope, Name("later"), 1, TimeSpan.FromMinutes(5), Token);
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("earlier"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        await coordinator.Fixture.ExecuteAsync(coordinator.Database.Name, $"UPDATE isk_directory.scopes SET due_at = NULL WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

        await Maintenance.SettleDirectoryAsync(scope, Token);

        Assert.Equal(await coordinator.Fixture.ScalarAsync<DateTime>(coordinator.Database.Name, $"SELECT expires_at FROM isk_coordination.leases WHERE scope_key = '{ScopeKey(scope)}'"), await DueAsync(scope));
        await coordinator.Leases(0).ReleaseAsync(lease, Token);
        await Maintenance.SettleDirectoryAsync(scope, Token);
        Assert.Equal(await coordinator.Fixture.ScalarAsync<DateTime>(coordinator.Database.Name, $"SELECT expires_at FROM isk_coordination.coordination_values WHERE scope_key = '{ScopeKey(scope)}'"), await DueAsync(scope));
    }

    [Fact]
    public async Task FenceRecordsAndNamespaceGenerationsDoNotKeepTheRow()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().ProjectA;
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("fenced"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Name("resource"), lease, Token));
        await coordinator.Cache(0).InvalidateAsync(scope, new CacheNamespace("settle", "entries"), Token);
        await coordinator.Leases(0).ReleaseAsync(lease, Token);

        await Maintenance.SettleDirectoryAsync(scope, Token);

        Assert.Equal(0L, await RowsAsync(scope));
        Assert.Equal(1L, await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.fences WHERE scope_key = '{ScopeKey(scope)}'"));
        Assert.Equal(1L, await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.cache_generations WHERE scope_key = '{ScopeKey(scope)}'"));
    }

    [Fact]
    public async Task ASettleOfAScopeWithNoRowChangesNothing()
    {
        coordinator.Require();
        var scope = new ConformanceScopes().OrganizationB;

        await Maintenance.SettleDirectoryAsync(scope, Token);

        Assert.Equal(0L, await RowsAsync(scope));
    }

    private static CoordinationName Name(string name) => new("settle", name);

    private static string ScopeKey(CoordinationScope scope) => CoordinationKey.ScopeKey(scope.Owner);

    private Task<long> RowsAsync(CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_directory.scopes WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

    private Task<DateTime> DueAsync(CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<DateTime>(coordinator.Database.Name, $"SELECT due_at FROM isk_directory.scopes WHERE listed_scope = '{ScopeKey(scope)}' AND module = 'coordination'");

    private Task<DateTime> EarliestAsync(CoordinationScope scope) => coordinator.Fixture.ScalarAsync<DateTime>(
        coordinator.Database.Name,
        $"SELECT least((SELECT min(expires_at) FROM isk_coordination.coordination_values WHERE scope_key = '{ScopeKey(scope)}'), (SELECT min(expires_at) FROM isk_coordination.leases WHERE scope_key = '{ScopeKey(scope)}'))");
}

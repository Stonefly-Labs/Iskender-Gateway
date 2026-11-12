using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

public class CoordinationMaintenanceTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(300);

    private readonly string _module = "purge_" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationMaintenance Maintenance => new(coordinator.Units(0), coordinator.CoordinationSettings);

    [Fact]
    public async Task PurgingRemovesOnlyTheGivenScopesExpiredRows()
    {
        coordinator.Require();
        var (mine, other) = (coordinator.Scopes.OrganizationA, coordinator.Scopes.OrganizationB);
        var atomic = coordinator.CreateClient(0).Atomic;
        await atomic.AddAsync(mine, Name("expired"), 1, Brief, Token);
        await atomic.AddAsync(other, Name("expired"), 1, Brief, Token);
        await coordinator.Leases(0).AcquireAsync(mine, Name("expired_lease"), Brief, LeaseWait.TryOnly, Token);
        await atomic.AddAsync(mine, Name("live"), 1, TimeSpan.FromMinutes(5), Token);
        var live = (await coordinator.Leases(0).AcquireAsync(mine, Name("live_lease"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        await Task.Delay(Brief + TimeSpan.FromMilliseconds(150), Token);
        var before = await CountsAsync();

        Assert.Equal(2, await Maintenance.PurgeExpiredAsync(mine, 100, Token));

        var after = await CountsAsync();
        Assert.Equal(before.Values - 1, after.Values);
        Assert.Equal(before.Leases - 1, after.Leases);
        Assert.Equal(new CoordinationValue(1, 1), await atomic.ReadAsync(mine, Name("live"), Token));
        Assert.Equal(1, await ExpiredValuesAsync(other));
        Assert.Equal(0, await Maintenance.PurgeExpiredAsync(mine, 100, Token));
        await coordinator.Leases(0).ReleaseAsync(live, Token);
    }

    [Fact]
    public async Task PurgingNeverDeletesAFenceOrGenerationRow()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.ProjectA;
        var lease = (await coordinator.Leases(0).AcquireAsync(scope, Name("fenced"), Brief, LeaseWait.TryOnly, Token)).Lease!;
        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Name("resource"), lease, Token));
        await coordinator.Fixture.ExecuteAsync(coordinator.Database.Name, $"INSERT INTO isk_coordination.cache_generations (scope_key, namespace_hash, generation) VALUES ('{CoordinationKey.ScopeKey(scope.Owner)}', '{new string('a', 64)}', 3)");
        await Task.Delay(Brief + TimeSpan.FromMilliseconds(150), Token);
        var before = await CountsAsync();

        Assert.Equal(1, await Maintenance.PurgeExpiredAsync(scope, 100, Token));

        var after = await CountsAsync();
        Assert.Equal(before.Leases - 1, after.Leases);
        Assert.Equal(before.Fences, after.Fences);
        Assert.Equal(before.Generations, after.Generations);
    }

    [Fact]
    public async Task PurgingWorksInBatchesNoLargerThanRequested()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.ProjectB;
        var atomic = coordinator.CreateClient(0).Atomic;
        for (var index = 0; index < 5; index++)
        {
            await atomic.AddAsync(scope, Name($"value_{index}"), 1, Brief, Token);
        }

        await Task.Delay(Brief + TimeSpan.FromMilliseconds(150), Token);

        Assert.Equal(2, await Maintenance.PurgeExpiredAsync(scope, 2, Token));
        Assert.Equal(3, await ExpiredValuesAsync(scope));
        Assert.Equal(2, await Maintenance.PurgeExpiredAsync(scope, 2, Token));
        Assert.Equal(1, await Maintenance.PurgeExpiredAsync(scope, 2, Token));
        Assert.Equal(0, await ExpiredValuesAsync(scope));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ABatchOfZeroOrLessIsRefused(int batch)
    {
        coordinator.Require();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Maintenance.PurgeExpiredAsync(coordinator.Scopes.OrganizationA, batch, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
    }

    private CoordinationName Name(string name) => new(_module, name);

    // Counted as the superuser, past row-level security, across every scope.
    private async Task<(long Values, long Leases, long Fences, long Generations)> CountsAsync() => (
        await CountAsync("coordination_values"),
        await CountAsync("leases"),
        await CountAsync("fences"),
        await CountAsync("cache_generations"));

    private Task<long> CountAsync(string table) =>
        coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.{table}");

    private Task<long> ExpiredValuesAsync(CoordinationScope scope) =>
        coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.coordination_values WHERE scope_key = '{CoordinationKey.ScopeKey(scope.Owner)}' AND expires_at <= clock_timestamp()");
}

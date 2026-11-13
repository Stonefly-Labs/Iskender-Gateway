using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Cache;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Iskender.Infrastructure.Redis.Tests.Leases;

namespace Iskender.Infrastructure.Redis.Tests.Cache;

/// <summary>The versioned cache suite against <c>RedisVersionedCache</c>, each client on a multiplexer of its own.</summary>
public sealed class RedisCacheConformanceTests(RedisFixture fixture) : VersionedCacheConformanceTests, IAsyncDisposable
{
    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItDeclaresLeased() =>
        Assert.Equal(ConsistencyLevel.Leased, RedisVersionedCache.Capabilities.LevelOf(CoordinationPrimitive.CacheInvalidation));

    [Fact]
    public async Task TheGenerationNeverExpiresAndAnEntryExpiresWithItsExpiry()
    {
        fixture.RequireRedis();
        var scope = _coordinator.Scopes.OrganizationA;
        var cache = _coordinator.CreateClient(0).Cache;
        await cache.InvalidateAsync(scope, Namespace, Token);
        var miss = await cache.TryGetAsync(scope, Namespace, "item", 1, Token);
        Assert.True(await cache.SetAsync(scope, Namespace, "item", 1, new byte[] { 1 }, TimeSpan.FromMinutes(5), miss.Stamp, Token));

        // The adapter's ACL grants no TTL or EXISTS, so the admin login reads them.
        var database = await fixture.AdminDatabaseAsync();

        Assert.True(await database.KeyExistsAsync(RedisScripts.GenerationKey(scope, Namespace)));
        Assert.Null(await database.KeyTimeToLiveAsync(RedisScripts.GenerationKey(scope, Namespace)));
        Assert.InRange(await database.KeyTimeToLiveAsync(RedisScripts.EntryKey(scope, Namespace, "item")) ?? TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task TheEntryHoldsTheValueAsBytesWithItsVersionAndGeneration()
    {
        fixture.RequireRedis();
        var scope = _coordinator.Scopes.OrganizationA;
        var cache = _coordinator.CreateClient(0).Cache;
        await cache.InvalidateAsync(scope, Namespace, Token);
        await cache.InvalidateAsync(scope, Namespace, Token);
        var value = new byte[] { 0, 255, 10, 13, 0 };
        var miss = await cache.TryGetAsync(scope, Namespace, "item", 7, Token);
        Assert.True(await cache.SetAsync(scope, Namespace, "item", 7, value, TimeSpan.FromMinutes(5), miss.Stamp, Token));
        var database = await fixture.AdminDatabaseAsync();

        var entry = await database.HashGetAsync(RedisScripts.EntryKey(scope, Namespace, "item"), ["ver", "gen", "val"]);

        Assert.Equal("7", entry[0].ToString());
        Assert.Equal("2", entry[1].ToString());
        Assert.Equal(value, (byte[])entry[2]!);
        Assert.Equal(value, (await _coordinator.CreateClient(1).Cache.TryGetAsync(scope, Namespace, "item", 7, Token)).Value.ToArray());
    }

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}

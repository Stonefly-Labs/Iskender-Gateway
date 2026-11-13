using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Cache;

/// <summary>
/// User Story 4 scenario 3 against Redis (FR-020, research R13): with the server stopped, a read is a miss within the
/// operation bound, writes event 5008, and throws nothing, while a failed set or invalidation throws, so a lost
/// invalidation is visible. The test stops a container of its own and starts it again before it is removed.
/// </summary>
public class CacheOutageTests
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(1);

    // Covers scheduling and the client noticing a closed socket.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly CacheNamespace _namespace = new("cache_outage", "entries");
    private readonly ConformanceScopes _scopes = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario3AReadDuringAnOutageIsAMissAndAFailedWriteThrows()
    {
        await using var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        var logger = new CapturingDiagnostics.Logger();
        var settings = TestCoordinationSettings.For(
            redis,
            redisOverrides: new Dictionary<string, object> { ["connectTimeout"] = "1s" },
            sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "1s" });
        await using var connection = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis), logger);
        var cache = new RedisVersionedCache(connection);
        var scope = _scopes.OrganizationA;
        var stamp = (await cache.TryGetAsync(scope, _namespace, "item", 1, Token)).Stamp;
        Assert.True(await cache.SetAsync(scope, _namespace, "item", 1, new byte[] { 1 }, Expiry, stamp, Token));

        await redis.StopAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            var lookup = await cache.TryGetAsync(scope, _namespace, "item", 1, Token);
            var elapsed = watch.Elapsed;

            Assert.False(lookup.IsHit);
            Assert.Null(lookup.Stamp);
            Assert.InRange(elapsed, TimeSpan.Zero, OperationTimeout + Allowance);
            Assert.Contains(logger.Events, logged => logged.Id.Id == 5008);

            var set = await Assert.ThrowsAsync<GatewayException>(async () => await cache.SetAsync(scope, _namespace, "item", 2, new byte[] { 2 }, Expiry, stamp, Token));
            Assert.Same(ErrorCodes.CoordinationUnavailable, set.Code);
            var bump = await Assert.ThrowsAsync<GatewayException>(async () => await cache.InvalidateAsync(scope, _namespace, Token));
            Assert.Same(ErrorCodes.CoordinationUnavailable, bump.Code);
        }
        finally
        {
            await redis.StartAsync();
        }
    }
}

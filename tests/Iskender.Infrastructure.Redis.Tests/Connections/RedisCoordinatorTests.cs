using System.Reflection;
using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Redis.Tests.Connections;

/// <summary>
/// TR-2 and FR-002: a host outside the adapter's assembly builds the four Redis ports over one connection through
/// <see cref="RedisCoordinator"/>. These tests use only the adapter's public surface, never its internals.
/// </summary>
public class RedisCoordinatorTests(RedisFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheFactoryAndThePortsItBuildsArePublic()
    {
        var factory = typeof(RedisCoordinator);
        var constructor = factory.GetConstructor([typeof(CoordinationSettings), typeof(ICoordinationCredentialProvider), typeof(ILogger), typeof(TimeProvider)]);

        Assert.True(factory.IsPublic);
        Assert.NotNull(constructor);
        Assert.True(constructor.IsPublic);
        Assert.All(
            factory.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.True(property.PropertyType.IsPublic && property.GetMethod!.IsPublic, property.Name));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(factory));
        Assert.Same(CoordinatorCapabilities.Redis, RedisCoordinator.Capabilities);
    }

    [Fact]
    public async Task ThePortsItBuildsWorkAgainstRedis()
    {
        fixture.RequireRedis();
        var scopes = new ConformanceScopes();
        var module = "factory_" + Guid.NewGuid().ToString("N")[..12];
        await using var coordinator = new RedisCoordinator(TestCoordinationSettings.For(fixture), TestCoordinationCredentialProvider.For(fixture));

        Assert.Equal(CoordinationHealthResult.Healthy, await coordinator.Health.CheckAsync(Token));
        Assert.Equal(new CoordinationValue(2, 1), await coordinator.Atomic.AddAsync(scopes.OrganizationA, new CoordinationName(module, "counter"), 2, TimeSpan.FromMinutes(1), Token));
        var acquisition = await coordinator.Leases.AcquireAsync(scopes.OrganizationA, new CoordinationName(module, "lease"), TimeSpan.FromSeconds(5), LeaseWait.TryOnly, Token);
        Assert.True(acquisition.IsAcquired);
        await coordinator.Leases.ReleaseAsync(acquisition.Lease!, Token);
        var cacheNamespace = new CacheNamespace(module, "entries");
        var miss = await coordinator.Cache.TryGetAsync(scopes.OrganizationA, cacheNamespace, "entry", 1, Token);
        Assert.False(miss.IsHit);
        Assert.True(await coordinator.Cache.SetAsync(scopes.OrganizationA, cacheNamespace, "entry", 1, new byte[] { 7 }, TimeSpan.FromMinutes(1), miss.Stamp, Token));
        Assert.True((await coordinator.Cache.TryGetAsync(scopes.OrganizationA, cacheNamespace, "entry", 1, Token)).IsHit);
    }
}

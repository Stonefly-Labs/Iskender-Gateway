using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Cache;

/// <summary>
/// The versioned cache conformance suite (FR-019, FR-021, FR-029): User Story 4 scenarios 1, 2, 4, and 5, the store
/// race, and the cache edge cases. A distributed implementation bumps a namespace from a second, independent client.
/// </summary>
public abstract class VersionedCacheConformanceTests
{
    protected VersionedCacheConformanceTests()
    {
        Module = "cache_" + Guid.NewGuid().ToString("N")[..12];
        Namespace = new CacheNamespace(Module, "entries");
    }

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    protected string Module { get; }

    protected CacheNamespace Namespace { get; }

    /// <summary>Extra time past an expiry, covering clock granularity on a distributed coordinator.</summary>
    protected virtual TimeSpan Slack => TimeSpan.FromMilliseconds(150);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    private static TimeSpan Expiry => TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Scenario1AnEntryStoredAsOneVersionIsAMissForAReaderExpectingAnother()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;
        Assert.True(await StoreAsync(cache, Scope, Namespace, "item", 1, [1, 2, 3]));

        Assert.False((await cache.TryGetAsync(Scope, Namespace, "item", 2, Token)).IsHit);
        var hit = await cache.TryGetAsync(Scope, Namespace, "item", 1, Token);
        Assert.True(hit.IsHit);
        Assert.Equal([1, 2, 3], hit.Value.ToArray());
    }

    [Fact]
    public async Task Scenario2AnotherClientsBumpMakesEveryEntryAMissForEveryClient()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        Assert.True(await StoreAsync(a.Cache, Scope, Namespace, "first", 1, [1]));
        Assert.True(await StoreAsync(a.Cache, Scope, Namespace, "second", 1, [2]));
        // A client with a store of its own caches the entry too; one sharing A's store already sees it.
        var seen = await b.Cache.TryGetAsync(Scope, Namespace, "first", 1, Token);
        Assert.True(seen.IsHit || await b.Cache.SetAsync(Scope, Namespace, "first", 1, new byte[] { 1 }, Expiry, seen.Stamp, Token));
        Assert.True((await b.Cache.TryGetAsync(Scope, Namespace, "first", 1, Token)).IsHit);

        await b.Cache.InvalidateAsync(Scope, Namespace, Token);

        foreach (var client in new[] { a, b })
        {
            Assert.False((await client.Cache.TryGetAsync(Scope, Namespace, "first", 1, Token)).IsHit);
            Assert.False((await client.Cache.TryGetAsync(Scope, Namespace, "second", 1, Token)).IsHit);
        }
    }

    [Fact]
    public async Task Scenario4AnotherOrganizationMissesTheSameModuleAndName()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;
        Assert.True(await StoreAsync(cache, Scope, Namespace, "item", 1, [9]));

        Assert.False((await cache.TryGetAsync(Coordinator.Scopes.OrganizationB, Namespace, "item", 1, Token)).IsHit);
        Assert.False((await cache.TryGetAsync(Coordinator.Scopes.ProjectA, Namespace, "item", 1, Token)).IsHit);
        Assert.True((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    [Fact]
    public async Task EachScopeHasItsOwnEntriesAndGeneration()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var scopes = Coordinator.Scopes.All;
        for (var index = 0; index < scopes.Count; index++)
        {
            Assert.True(await StoreAsync(a.Cache, scopes[index], Namespace, "item", 1, [(byte)index]));
        }

        for (var index = 0; index < scopes.Count; index++)
        {
            var hit = await a.Cache.TryGetAsync(scopes[index], Namespace, "item", 1, Token);
            Assert.True(hit.IsHit, $"Scope {index} lost its entry.");
            Assert.Equal([(byte)index], hit.Value.ToArray());
        }

        // A bump in one scope, from another client, leaves every other scope's entry a hit.
        await b.Cache.InvalidateAsync(scopes[0], Namespace, Token);
        Assert.False((await a.Cache.TryGetAsync(scopes[0], Namespace, "item", 1, Token)).IsHit);
        for (var index = 1; index < scopes.Count; index++)
        {
            Assert.True((await a.Cache.TryGetAsync(scopes[index], Namespace, "item", 1, Token)).IsHit, $"Scope 0's bump reached scope {index}.");
        }
    }

    [Fact]
    public async Task Scenario5AValueAboveTheBoundFailsAndNothingIsStored()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;
        var miss = await cache.TryGetAsync(Scope, Namespace, "item", 1, Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await cache.SetAsync(Scope, Namespace, "item", 1, new byte[Coordinator.Settings.MaximumValueBytes + 1], Expiry, miss.Stamp, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.False((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
        Assert.True(await cache.SetAsync(Scope, Namespace, "item", 1, new byte[Coordinator.Settings.MaximumValueBytes], Expiry, miss.Stamp, Token));
    }

    [Fact]
    public async Task AStoreRacingABumpStoresNothing()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var miss = await a.Cache.TryGetAsync(Scope, Namespace, "item", 1, Token);
        Assert.NotNull(miss.Stamp);

        await b.Cache.InvalidateAsync(Scope, Namespace, Token);

        Assert.False(await a.Cache.SetAsync(Scope, Namespace, "item", 1, new byte[] { 1 }, Expiry, miss.Stamp, Token));
        Assert.False((await a.Cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    [Fact]
    public async Task AFailureMissesEmptyStampStoresNothing()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;

        Assert.Null(CacheLookup.Failed.Stamp);
        Assert.False(await cache.SetAsync(Scope, Namespace, "item", 1, new byte[] { 1 }, Expiry, CacheLookup.Failed.Stamp, Token));
        Assert.False((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    [Fact]
    public async Task AnExpiredEntryIsAMiss()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;
        var brief = TimeSpan.FromMilliseconds(300);
        var miss = await cache.TryGetAsync(Scope, Namespace, "item", 1, Token);
        Assert.True(await cache.SetAsync(Scope, Namespace, "item", 1, new byte[] { 1 }, brief, miss.Stamp, Token));
        Assert.True((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);

        await Coordinator.PassTimeAsync(brief + Slack);

        Assert.False((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    [Fact]
    public async Task AnEntrySurvivesAnotherNamespacesBump()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var other = new CacheNamespace(Module, "other");
        Assert.True(await StoreAsync(a.Cache, Scope, Namespace, "item", 1, [1]));

        await b.Cache.InvalidateAsync(Scope, other, Token);

        Assert.True((await a.Cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    [Fact]
    public async Task EveryBumpWritesEvent5007()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(1).Cache;

        for (var bump = 0; bump < 3; bump++)
        {
            await cache.InvalidateAsync(Scope, Namespace, Token);
        }

        Assert.Equal(3, CapturingDiagnostics.Shared.Logs.Count(logged => logged.Id.Id == 5007 && IsThisModule(logged)));
    }

    [Fact]
    public async Task NoCachedValueAppearsInAnyEventTagOrLabel()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        Assert.True(await StoreAsync(a.Cache, Scope, Namespace, "item", 1, Canary.Bytes));
        var hit = await a.Cache.TryGetAsync(Scope, Namespace, "item", 1, Token);
        Assert.True(hit.IsHit);
        CapturingDiagnostics.Shared.Report(hit);
        await b.Cache.InvalidateAsync(Scope, Namespace, Token);
        CapturingDiagnostics.Shared.Report(await a.Cache.TryGetAsync(Scope, Namespace, "item", 1, Token));

        Assert.Empty(CapturingDiagnostics.Shared.CanaryHits());
    }

    [Fact]
    public async Task AnInvalidExpiryIsRefused()
    {
        Coordinator.Require();
        var cache = Coordinator.CreateClient(0).Cache;
        var miss = await cache.TryGetAsync(Scope, Namespace, "item", 1, Token);

        foreach (var expiry in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), Coordinator.Settings.MaximumExpiry + TimeSpan.FromSeconds(1) })
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(async () => await cache.SetAsync(Scope, Namespace, "item", 1, new byte[] { 1 }, expiry, miss.Stamp, Token));
            Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        }

        Assert.False((await cache.TryGetAsync(Scope, Namespace, "item", 1, Token)).IsHit);
    }

    private static async Task<bool> StoreAsync(IVersionedCache cache, CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, byte[] value)
    {
        var miss = await cache.TryGetAsync(scope, cacheNamespace, name, version, Token);
        Assert.False(miss.IsHit);
        return await cache.SetAsync(scope, cacheNamespace, name, version, value, Expiry, miss.Stamp, Token);
    }

    private bool IsThisModule(CapturedLog logged) => logged.Fields.Any(field => field.Key == "Module" && Equals(field.Value, Module));
}

using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Cache;

/// <summary>The bounded in-memory store (FR-019a): least recently used eviction, the bound, expiry, and concurrent access.</summary>
public class InMemoryCacheStoreTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    [Fact]
    public void TheLeastRecentlyUsedEntryIsEvictedFirst()
    {
        var store = new InMemoryCacheStore(3);
        store.Set("a", 1, 0, new byte[] { 1 }, Expiry);
        store.Set("b", 1, 0, new byte[] { 2 }, Expiry);
        store.Set("c", 1, 0, new byte[] { 3 }, Expiry);

        // Reading "a" makes "b" the least recently used.
        Assert.NotNull(store.Get("a"));
        store.Set("d", 1, 0, new byte[] { 4 }, Expiry);

        Assert.Null(store.Get("b"));
        Assert.NotNull(store.Get("a"));
        Assert.NotNull(store.Get("c"));
        Assert.NotNull(store.Get("d"));
    }

    [Fact]
    public void ReplacingAnEntryMakesItTheMostRecentlyUsedWithoutGrowingTheStore()
    {
        var store = new InMemoryCacheStore(2);
        store.Set("a", 1, 0, new byte[] { 1 }, Expiry);
        store.Set("b", 1, 0, new byte[] { 2 }, Expiry);
        store.Set("a", 2, 0, new byte[] { 3 }, Expiry);
        store.Set("c", 1, 0, new byte[] { 4 }, Expiry);

        Assert.Equal(2, store.Count);
        Assert.Null(store.Get("b"));
        var replaced = store.Get("a");
        Assert.NotNull(replaced);
        Assert.Equal(2, replaced.Version);
        Assert.Equal([3], replaced.Value.ToArray());
    }

    [Fact]
    public void TheStoreNeverHoldsMoreThanItsBound()
    {
        var store = new InMemoryCacheStore(10);
        for (var index = 0; index < 100; index++)
        {
            store.Set("entry" + index, 1, 0, new byte[] { 1 }, Expiry);
            Assert.InRange(store.Count, 1, 10);
        }

        Assert.Equal(10, store.Count);
        Assert.NotNull(store.Get("entry99"));
        Assert.Null(store.Get("entry89"));
    }

    [Fact]
    public void AnEntryPastItsExpiryIsGoneAndNoLongerCounted()
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryCacheStore(4, clock);
        store.Set("a", 1, 0, new byte[] { 1 }, TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.NotNull(store.Get("a"));
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Null(store.Get("a"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TheStoreKeepsACopyOfTheValue()
    {
        var store = new InMemoryCacheStore(1);
        var value = new byte[] { 1, 2 };
        store.Set("a", 1, 7, value, Expiry);
        value[0] = 9;

        var entry = store.Get("a");
        Assert.NotNull(entry);
        Assert.Equal([1, 2], entry.Value.ToArray());
        Assert.Equal(7, entry.Generation);
    }

    [Fact]
    public async Task ConcurrentReadersAndWritersKeepTheBoundAndSeeWholeEntries()
    {
        const int Bound = 16;
        var store = new InMemoryCacheStore(Bound);
        var token = TestContext.Current.CancellationToken;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 2_000; index++)
            {
                var key = "entry" + (index % 40);
                var marker = (byte)worker;
                store.Set(key, worker, worker, new byte[] { marker, marker }, Expiry);
                if (store.Get(key) is { } entry)
                {
                    // An entry is never torn between two writers.
                    Assert.Equal(entry.Version, entry.Generation);
                    Assert.Equal((byte)entry.Version, entry.Value.Span[0]);
                    Assert.Equal(entry.Value.Span[0], entry.Value.Span[1]);
                }

                Assert.InRange(store.Count, 0, Bound);
            }
        }, token)));

        Assert.Equal(Bound, store.Count);
    }

    [Fact]
    public void ABoundBelowOneIsRefused()
    {
        var failure = Assert.Throws<GatewayException>(() => new InMemoryCacheStore(0));
        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
    }
}

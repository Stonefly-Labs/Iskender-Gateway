using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CacheLookupTests
{
    [Fact]
    public void AHitCarriesItsValueAndNoStamp()
    {
        var lookup = CacheLookup.Hit(new byte[] { 1, 2, 3 });

        Assert.True(lookup.IsHit);
        Assert.Equal([1, 2, 3], lookup.Value.ToArray());
        Assert.Null(lookup.Stamp);
    }

    [Fact]
    public void AMissCarriesTheGenerationItObserved()
    {
        var lookup = CacheLookup.Miss(CacheStamp.Of(7));

        Assert.False(lookup.IsHit);
        Assert.True(lookup.Value.IsEmpty);
        Assert.Equal(CacheStamp.Of(7), lookup.Stamp);
        Assert.Equal(7, lookup.Stamp!.Value.Generation);
    }

    [Fact]
    public void AFailureMissCarriesNoStamp()
    {
        Assert.False(CacheLookup.Failed.IsHit);
        Assert.True(CacheLookup.Failed.Value.IsEmpty);
        Assert.Null(CacheLookup.Failed.Stamp);
    }

    [Fact]
    public void NoTextShowsTheValueOrGeneration()
    {
        var canary = "canary-7f3a"u8.ToArray();

        Assert.Equal("hit", CacheLookup.Hit(canary).ToString());
        Assert.Equal("miss", CacheLookup.Miss(CacheStamp.Of(12345)).ToString());
        Assert.Equal("miss after failure", CacheLookup.Failed.ToString());
        Assert.DoesNotContain("12345", CacheStamp.Of(12345).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StampsCompareByGeneration()
    {
        Assert.Equal(CacheStamp.Of(3), CacheStamp.Of(3));
        Assert.NotEqual(CacheStamp.Of(3), CacheStamp.Of(4));
    }
}

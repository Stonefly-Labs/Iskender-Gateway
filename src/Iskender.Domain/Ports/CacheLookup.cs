namespace Iskender.Domain.Ports;

/// <summary>
/// A cache read's result: a hit with the value, or a miss with the namespace generation it observed. A miss caused by a
/// coordinator failure carries no stamp, so a value read afterwards from the source is never stored (contract Versioned
/// cache rule 1). Owned by ISK-0005.
/// </summary>
public sealed class CacheLookup
{
    private CacheLookup(bool isHit, ReadOnlyMemory<byte> value, CacheStamp? stamp)
    {
        IsHit = isHit;
        Value = value;
        Stamp = stamp;
    }

    /// <summary>A miss after a coordinator failure, with no stamp.</summary>
    public static CacheLookup Failed { get; } = new(isHit: false, ReadOnlyMemory<byte>.Empty, stamp: null);

    public bool IsHit { get; }

    /// <summary>The cached value on a hit, and empty on a miss.</summary>
    public ReadOnlyMemory<byte> Value { get; }

    /// <summary>The generation a miss observed, to pass to <see cref="IVersionedCache.SetAsync"/>, or <c>null</c> on a hit or a failure.</summary>
    public CacheStamp? Stamp { get; }

    public static CacheLookup Hit(ReadOnlyMemory<byte> value) => new(isHit: true, value, stamp: null);

    public static CacheLookup Miss(CacheStamp stamp) => new(isHit: false, ReadOnlyMemory<byte>.Empty, stamp);

    public override string ToString() => IsHit ? "hit" : Stamp is null ? "miss after failure" : "miss";
}

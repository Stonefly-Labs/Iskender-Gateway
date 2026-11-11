namespace Iskender.Domain.Ports;

/// <summary>
/// The namespace generation a cache miss observed. Callers pass it back unchanged to
/// <see cref="IVersionedCache.SetAsync"/>; only coordinators create or read it (contract Versioned cache rule 1).
/// Owned by ISK-0005.
/// </summary>
public readonly record struct CacheStamp
{
    private CacheStamp(long generation)
    {
        Generation = generation;
    }

    /// <summary>For coordinators: the observed generation, zero for a namespace never bumped.</summary>
    public long Generation { get; }

    /// <summary>For coordinators: a stamp for an observed generation.</summary>
    public static CacheStamp Of(long generation) => new(generation);

    public override string ToString() => "stamp";
}

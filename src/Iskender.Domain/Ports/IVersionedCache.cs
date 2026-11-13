namespace Iskender.Domain.Ports;

/// <summary>
/// A scoped cache whose entries carry a version and whose namespaces carry a generation (FR-019 to FR-021, TR-11). It is
/// never authoritative: any coordinator failure makes a read a miss with no stamp, and the caller reads the source of
/// truth. Values are bounded by <c>cache.maximumValueBytes</c>, never logged, and only ever served in their own scope.
/// Owned by ISK-0005.
/// </summary>
public interface IVersionedCache
{
    /// <summary>
    /// A hit when an entry of <paramref name="expectedVersion"/> was stored under the namespace's current generation, and
    /// otherwise a miss carrying the generation it observed. A coordinator failure is a miss with no stamp, writes event
    /// 5008, and throws nothing.
    /// </summary>
    ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="value"/> as <paramref name="version"/> with <paramref name="expiry"/>, only when the
    /// namespace's generation still equals <paramref name="stamp"/>, and returns whether it stored. A <c>null</c> stamp
    /// stores nothing. A value above <c>cache.maximumValueBytes</c> fails with <c>core.value.invalid</c>. A coordinator
    /// failure throws, so the caller sees it.
    /// </summary>
    ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken);

    /// <summary>
    /// Bumps the namespace's generation atomically, so every entry in it is a miss for every client, and writes event
    /// 5007. A coordinator failure throws, so a lost invalidation is visible to its caller.
    /// </summary>
    ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken);
}

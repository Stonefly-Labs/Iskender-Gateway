using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// A bounded, thread-safe map of cache entries for one process (FR-019a): at most <c>cache.maximumEntries</c>, evicting
/// the least recently used, with expiry on the injected <see cref="TimeProvider"/>. Keys are derived hashes, never scope
/// text, so the store holds no organization or project ID. It stores a copy of each value. Public because the PostgreSQL
/// cache keeps its values here too. Owned by ISK-0005.
/// </summary>
public sealed class InMemoryCacheStore
{
    private readonly int _maximumEntries;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<Item>> _items = new(StringComparer.Ordinal);

    // Most recently used first.
    private readonly LinkedList<Item> _order = new();

    public InMemoryCacheStore(int maximumEntries, TimeProvider? timeProvider = null)
    {
        _maximumEntries = maximumEntries > 0 ? maximumEntries : throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>The unexpired entry at <paramref name="key"/>, which becomes the most recently used, or <c>null</c>.</summary>
    public CacheStoreEntry? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (!_items.TryGetValue(key, out var node))
            {
                return null;
            }

            if (_timeProvider.GetTimestamp() >= node.Value.ExpiresAt)
            {
                Remove(node);
                return null;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Entry;
        }
    }

    /// <summary>Stores a copy of <paramref name="value"/> at <paramref name="key"/>, evicting the least recently used entry when full.</summary>
    public void Set(string key, long version, long generation, ReadOnlyMemory<byte> value, TimeSpan expiry)
    {
        ArgumentNullException.ThrowIfNull(key);
        var item = new Item(key, new CacheStoreEntry(version, generation, value.ToArray()), _timeProvider.GetTimestamp() + (long)(expiry.TotalSeconds * _timeProvider.TimestampFrequency));
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var existing))
            {
                Remove(existing);
            }

            while (_items.Count >= _maximumEntries)
            {
                Remove(_order.Last!);
            }

            _items[key] = _order.AddFirst(item);
        }
    }

    private void Remove(LinkedListNode<Item> node)
    {
        _order.Remove(node);
        _items.Remove(node.Value.Key);
    }

    private sealed record Item(string Key, CacheStoreEntry Entry, long ExpiresAt);
}

/// <summary>One stored entry: its version, the namespace generation it was stored under, and its value. Owned by ISK-0005.</summary>
public sealed record CacheStoreEntry(long Version, long Generation, ReadOnlyMemory<byte> Value);

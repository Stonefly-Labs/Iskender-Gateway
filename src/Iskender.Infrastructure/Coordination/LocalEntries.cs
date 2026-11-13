using System.Collections.Concurrent;

namespace Iskender.Infrastructure.Coordination;

/// <summary>One key's state in a local substitute, with its own lock. An idle entry holds nothing and may be removed.</summary>
internal abstract class LocalEntry
{
    /// <summary>How often, at most, a substitute's operation sweeps its idle entries.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    public Lock Gate { get; } = new();

    /// <summary>Set under <see cref="Gate"/> when the entry leaves the map; a caller that meets it looks the key up again.</summary>
    public bool Removed { get; set; }

    /// <summary>Whether the entry holds nothing at <paramref name="now"/>: absent, expired, or released.</summary>
    public abstract bool IsIdle(long now);
}

/// <summary>
/// The keyed entries of a local substitute, so that its memory is bounded by expiry (contract Known limits, FR-013). Work
/// on a key runs under that key's lock; an entry left idle is removed at once, and a sweep, at most once per
/// <see cref="LocalEntry.SweepInterval"/> and only from a caller's operation, removes the idle entries nobody touched again. Removal
/// marks the entry under its lock, so a caller holding a removed entry retries on a fresh one and the per-key lock holds.
/// Owned by ISK-0005.
/// </summary>
internal sealed class LocalEntries<TEntry>(TimeProvider timeProvider)
    where TEntry : LocalEntry, new()
{
    private readonly ConcurrentDictionary<string, TEntry> _entries = new(StringComparer.Ordinal);
    private long _nextSweep = timeProvider.GetTimestamp() + Ticks(timeProvider, LocalEntry.SweepInterval);
    private int _sweeping;

    /// <summary>How many entries the map holds, so a test can prove expired entries are freed.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Runs <paramref name="work"/> on the key's entry under its lock. Without <paramref name="create"/>, an absent key runs
    /// it with <c>null</c> and adds nothing.
    /// </summary>
    public T Run<T>(string key, bool create, Func<TEntry?, long, T> work)
    {
        var result = Locked(key, create, work);
        Sweep();
        return result;
    }

    private T Locked<T>(string key, bool create, Func<TEntry?, long, T> work)
    {
        while (true)
        {
            TEntry? entry;
            if (create)
            {
                entry = _entries.GetOrAdd(key, static _ => new TEntry());
            }
            else if (!_entries.TryGetValue(key, out entry))
            {
                return work(null, timeProvider.GetTimestamp());
            }

            lock (entry.Gate)
            {
                if (entry.Removed)
                {
                    continue;
                }

                var now = timeProvider.GetTimestamp();
                try
                {
                    return work(entry, now);
                }
                finally
                {
                    RemoveIfIdle(key, entry, now);
                }
            }
        }
    }

    // Never waits on a key's lock: an entry in use is left for the next sweep.
    private void Sweep()
    {
        var now = timeProvider.GetTimestamp();
        if (now < Volatile.Read(ref _nextSweep) || Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Volatile.Write(ref _nextSweep, now + Ticks(timeProvider, LocalEntry.SweepInterval));
            foreach (var (key, entry) in _entries)
            {
                if (!entry.Gate.TryEnter())
                {
                    continue;
                }

                try
                {
                    RemoveIfIdle(key, entry, timeProvider.GetTimestamp());
                }
                finally
                {
                    entry.Gate.Exit();
                }
            }
        }
        finally
        {
            Volatile.Write(ref _sweeping, 0);
        }
    }

    // Called under the entry's lock.
    private void RemoveIfIdle(string key, TEntry entry, long now)
    {
        if (!entry.Removed && entry.IsIdle(now))
        {
            entry.Removed = true;
            _entries.TryRemove(new KeyValuePair<string, TEntry>(key, entry));
        }
    }

    private static long Ticks(TimeProvider timeProvider, TimeSpan duration) => (long)(duration.TotalSeconds * timeProvider.TimestampFrequency);
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The local substitute for the versioned cache (FR-005, FR-010): generations and entries in this process's memory, so
/// it declares <c>local</c> and is never reported as distributed. Entries live in a bounded <see cref="InMemoryCacheStore"/>;
/// one lock makes a generation check and a store, or a bump, atomic. It has no coordinator to lose, so it never degrades.
/// Owned by ISK-0005.
/// </summary>
public sealed class LocalVersionedCache : IVersionedCache
{
    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly InMemoryCacheStore _store;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _generations = new(StringComparer.Ordinal);

    public LocalVersionedCache(CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _store = new InMemoryCacheStore(settings.MaximumEntries, _timeProvider);
    }

    /// <summary>What this substitute declares: <c>local</c> for every primitive.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Local;

    public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(cacheNamespace.Module, "get", () =>
        {
            lock (_gate)
            {
                var generation = _generations.GetValueOrDefault(CachePolicy.NamespaceKey(scope, cacheNamespace));
                var entry = _store.Get(CachePolicy.EntryKey(scope, cacheNamespace, name));
                return ValueTask.FromResult(entry is not null && entry.Version == expectedVersion && entry.Generation == generation
                    ? CacheLookup.Hit(entry.Value)
                    : CacheLookup.Miss(CacheStamp.Of(generation)));
            }
        });
    }

    public ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        CachePolicy.RequireEntry(_settings, value, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        if (stamp is not { } observed)
        {
            return ValueTask.FromResult(false);
        }

        return Measure(cacheNamespace.Module, "set", () =>
        {
            lock (_gate)
            {
                if (_generations.GetValueOrDefault(CachePolicy.NamespaceKey(scope, cacheNamespace)) != observed.Generation)
                {
                    return ValueTask.FromResult(false);
                }

                _store.Set(CachePolicy.EntryKey(scope, cacheNamespace, name), version, observed.Generation, value, expiry);
                return ValueTask.FromResult(true);
            }
        });
    }

    public async ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw new GatewayException(ErrorCodes.CoordinationScopeRequired);
        }

        if (cacheNamespace is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        await Measure(cacheNamespace.Module, "invalidate", () =>
        {
            lock (_gate)
            {
                var key = CachePolicy.NamespaceKey(scope, cacheNamespace);
                _generations[key] = _generations.GetValueOrDefault(key) + 1;
            }

            return ValueTask.FromResult(true);
        });
        CoordinationLog.LogCacheInvalidated(_logger, cacheNamespace.Module, CoordinatorKind.Local);
    }

    private ValueTask<T> Measure<T>(string module, string operation, Func<ValueTask<T>> work) =>
        CoordinationTelemetry.MeasureAsync(module, CoordinationPrimitive.CacheInvalidation, operation, CoordinatorKind.Local, _timeProvider, work);
}

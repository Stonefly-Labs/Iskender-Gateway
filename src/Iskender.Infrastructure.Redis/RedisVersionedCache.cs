using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// The versioned cache on Redis (FR-019 to FR-021, research R10 and R13): each entry is a hash <c>{ver, gen, val}</c> under
/// its namespace's hash tag, next to the namespace's generation key, so one script reads the generation and the entry, or
/// checks the generation and stores, atomically. The generation key never expires. Values go to the server as bytes and
/// are never logged. A failover can lose a bump, so it declares <c>leased</c>. Owned by ISK-0005.
/// </summary>
public sealed class RedisVersionedCache : IVersionedCache
{
    private readonly RedisConnection _connection;
    private readonly TimeProvider _timeProvider;

    internal RedisVersionedCache(RedisConnection connection, TimeProvider? timeProvider = null)
    {
        _connection = connection ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>What this coordinator declares: <c>leased</c> for cache invalidation.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Redis;

    public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return CachePolicy.ReadOrMissAsync(_connection.Logger, cacheNamespace.Module, CoordinatorKind.Redis, async () =>
        {
            var result = (RedisResult[])(await RunAsync(scope, cacheNamespace, RedisScripts.CacheGet, [RedisScripts.EntryKey(scope, cacheNamespace, name), RedisScripts.GenerationKey(scope, cacheNamespace)], [expectedVersion], cancellationToken))!;
            return (long)result[0] == 1
                ? CacheLookup.Hit((byte[])result[1]!)
                : CacheLookup.Miss(CacheStamp.Of((long)result[1]));
        }, cancellationToken);
    }

    public async ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        CachePolicy.RequireEntry(_connection.Settings, value, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        if (stamp is not { } observed)
        {
            return false;
        }

        var result = await RunAsync(
            scope,
            cacheNamespace,
            RedisScripts.CacheSet,
            [RedisScripts.EntryKey(scope, cacheNamespace, name), RedisScripts.GenerationKey(scope, cacheNamespace)],
            [version, value, (long)Math.Ceiling(expiry.TotalMilliseconds), observed.Generation],
            cancellationToken);
        return (long)result == 1;
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
        await RunAsync(scope, cacheNamespace, RedisScripts.CacheInvalidate, [RedisScripts.GenerationKey(scope, cacheNamespace)], [], cancellationToken);
        CoordinationLog.LogCacheInvalidated(_connection.Logger, cacheNamespace.Module, CoordinatorKind.Redis);
    }

    private ValueTask<RedisResult> RunAsync(CoordinationScope scope, CacheNamespace cacheNamespace, RedisScript script, RedisKey[] keys, RedisValue[] arguments, CancellationToken cancellationToken) =>
        _connection.RunAsync(scope, cacheNamespace.Module, CoordinationPrimitive.CacheInvalidation, script, keys, arguments, cancellationToken);
}

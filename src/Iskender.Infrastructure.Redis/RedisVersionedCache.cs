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
            return await RunAsync(scope, cacheNamespace, RedisScripts.CacheGet, [RedisScripts.EntryKey(scope, cacheNamespace, name), RedisScripts.GenerationKey(scope, cacheNamespace)], [expectedVersion], LookupReply, cancellationToken);
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

        return await RunAsync(
            scope,
            cacheNamespace,
            RedisScripts.CacheSet,
            [RedisScripts.EntryKey(scope, cacheNamespace, name), RedisScripts.GenerationKey(scope, cacheNamespace)],
            [version, value, (long)Math.Ceiling(expiry.TotalMilliseconds), observed.Generation],
            RedisReplies.Flag,
            cancellationToken);
    }

    public async ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationScopeRequired, CoordinationFailureClass.ScopeViolation);
        }

        if (cacheNamespace is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        await RunAsync(scope, cacheNamespace, RedisScripts.CacheInvalidate, [RedisScripts.GenerationKey(scope, cacheNamespace)], [], RedisReplies.Integer, cancellationToken);
        CoordinationLog.LogCacheInvalidated(_connection.Logger, cacheNamespace.Module, CoordinatorKind.Redis);
    }

    /// <summary>The get script's <c>{1, value}</c> on a hit and <c>{0, generation}</c> on a miss.</summary>
    internal static CacheLookup LookupReply(RedisResult reply)
    {
        var elements = RedisReplies.Array(reply, 2);
        return RedisReplies.Flag(elements[0])
            ? CacheLookup.Hit(RedisReplies.Bytes(elements[1]))
            : CacheLookup.Miss(CacheStamp.Of(RedisReplies.Integer(elements[1])));
    }

    private ValueTask<T> RunAsync<T>(CoordinationScope scope, CacheNamespace cacheNamespace, RedisScript script, RedisKey[] keys, RedisValue[] arguments, Func<RedisResult, T> parse, CancellationToken cancellationToken) =>
        _connection.RunAsync(scope, cacheNamespace.Module, CoordinationPrimitive.CacheInvalidation, script, keys, arguments, parse, cancellationToken);
}

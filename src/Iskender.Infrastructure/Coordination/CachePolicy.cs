using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// What every versioned cache does the same way (FR-019 to FR-021, research R13): the argument checks before any I/O, and
/// the key an entry has in an <see cref="InMemoryCacheStore"/>. Public because the Redis and PostgreSQL caches use it
/// too. Owned by ISK-0005.
/// </summary>
public static class CachePolicy
{
    /// <summary>
    /// A <c>null</c> scope fails with <c>coordination.scope_required</c>; a <c>null</c> namespace, or a name that is not a
    /// valid state name, with <c>core.value.invalid</c>.
    /// </summary>
    public static void RequireTarget(CoordinationScope scope, CacheNamespace cacheNamespace, string name)
    {
        if (scope is null)
        {
            throw new GatewayException(ErrorCodes.CoordinationScopeRequired);
        }

        if (cacheNamespace is null || name is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _ = CoordinationKey.Hash(scope.Owner, cacheNamespace.Module, name);
    }

    /// <summary>A value above <c>cache.maximumValueBytes</c>, or an invalid expiry, fails with <c>core.value.invalid</c>.</summary>
    public static void RequireEntry(CoordinationSettings settings, ReadOnlyMemory<byte> value, TimeSpan expiry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (value.Length > settings.MaximumValueBytes)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        AtomicPolicy.RequireExpiry(settings, expiry);
    }

    /// <summary>The namespace's key: a derived hash, never scope text.</summary>
    public static string NamespaceKey(CoordinationScope scope, CacheNamespace cacheNamespace) =>
        CoordinationKey.Hash(scope.Owner, cacheNamespace.Module, cacheNamespace.Name);

    /// <summary>An entry's key in an <see cref="InMemoryCacheStore"/>: its namespace's hash and its own, as Redis keys it.</summary>
    public static string EntryKey(CoordinationScope scope, CacheNamespace cacheNamespace, string name) =>
        NamespaceKey(scope, cacheNamespace) + ":" + CoordinationKey.Hash(scope.Owner, cacheNamespace.Module, name);
}

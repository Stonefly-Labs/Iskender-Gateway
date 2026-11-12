using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>One Lua script. The hash the server knows it by is the one <c>SCRIPT LOAD</c> returns.</summary>
internal sealed class RedisScript
{
    public RedisScript(string name, string text, bool writes, bool inline = false)
    {
        Name = name;
        Text = text;
        Writes = writes;
        Inline = inline;
    }

    /// <summary>The operation name used in diagnostics.</summary>
    public string Name { get; }

    public string Text { get; }

    /// <summary>Whether the script can change a key, so a failure after sending leaves the outcome unknown.</summary>
    public bool Writes { get; }

    /// <summary>Whether the script is sent with <c>EVAL</c> and its text, never loaded, so that no <c>SCRIPT LOAD</c> can delay it.</summary>
    public bool Inline { get; }
}

/// <summary>
/// The Lua scripts for every Redis operation, and the only place key names are built (ISK-0005 research R10, data model
/// "Redis keys"). Each script runs atomically on keys that share one hash tag. It is loaded once with <c>SCRIPT LOAD</c>
/// and called with <c>EVALSHA</c>, falling back to <c>EVAL</c> on <c>NOSCRIPT</c>. Scripts call only the commands the
/// contract's ACL grants. Lua numbers are doubles, so every value, amount, and limit stays within
/// <see cref="MaximumMagnitude"/>, and a script refuses a result beyond it without changing anything. Owned by ISK-0005.
/// </summary>
internal static partial class RedisScripts
{
    /// <summary>The largest magnitude a Lua number holds exactly, 2^53 - 1.</summary>
    public const long MaximumMagnitude = 9_007_199_254_740_991;

    /// <summary>
    /// Adds ARGV[1] to the value at KEYS[1], creating it with expiry ARGV[2] ms. Returns {1, value, version}, or {0, 0, 0}
    /// without changing anything when the result would leave the exact range.
    /// </summary>
    public static readonly RedisScript Add = new("atomic.add", """
        local v = redis.call('HGET', KEYS[1], 'v')
        local n
        local ver
        if v then
          n = tonumber(v) + tonumber(ARGV[1])
          ver = tonumber(redis.call('HGET', KEYS[1], 'ver')) + 1
        else
          n = tonumber(ARGV[1])
          ver = 1
        end
        if math.abs(n) > 9007199254740991 then return {0, 0, 0} end
        redis.call('HSET', KEYS[1], 'v', string.format('%d', n), 'ver', ver)
        if not v then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        return {1, n, ver}
        """, writes: true);

    /// <summary>
    /// Reserves ARGV[1] at KEYS[1] if value + amount &lt;= ARGV[2], creating the key with expiry ARGV[3] ms. Returns
    /// {1, value, version} when granted and {0, value, version} when denied; a denied reservation changes nothing.
    /// </summary>
    public static readonly RedisScript Reserve = new("atomic.reserve", """
        local v = redis.call('HGET', KEYS[1], 'v')
        local current = 0
        local ver = 0
        if v then
          current = tonumber(v)
          ver = tonumber(redis.call('HGET', KEYS[1], 'ver'))
        end
        local n = current + tonumber(ARGV[1])
        if n > tonumber(ARGV[2]) or math.abs(n) > 9007199254740991 then return {0, current, ver} end
        redis.call('HSET', KEYS[1], 'v', string.format('%d', n), 'ver', ver + 1)
        if not v then redis.call('PEXPIRE', KEYS[1], ARGV[3]) end
        return {1, n, ver + 1}
        """, writes: true);

    /// <summary>Subtracts ARGV[1] from the value at KEYS[1], never below 0. Returns {value, version}, or nil for an absent key.</summary>
    public static readonly RedisScript Release = new("atomic.release", """
        local v = redis.call('HGET', KEYS[1], 'v')
        if not v then return nil end
        local n = tonumber(v) - tonumber(ARGV[1])
        if n < 0 then n = 0 end
        local ver = tonumber(redis.call('HGET', KEYS[1], 'ver')) + 1
        redis.call('HSET', KEYS[1], 'v', string.format('%d', n), 'ver', ver)
        return {n, ver}
        """, writes: true);

    /// <summary>
    /// Sets the value at KEYS[1] to ARGV[2] if its version is ARGV[1] (0 for an absent key), creating it with expiry ARGV[3]
    /// ms. Returns 1 when set and 0 otherwise.
    /// </summary>
    public static readonly RedisScript CompareAndSet = new("atomic.compare_and_set", """
        local ver = redis.call('HGET', KEYS[1], 'ver')
        local current = 0
        if ver then current = tonumber(ver) end
        if current ~= tonumber(ARGV[1]) then return 0 end
        redis.call('HSET', KEYS[1], 'v', ARGV[2], 'ver', current + 1)
        if not ver then redis.call('PEXPIRE', KEYS[1], ARGV[3]) end
        return 1
        """, writes: true);

    /// <summary>Returns {value, version} at KEYS[1], or nil for an absent key.</summary>
    public static readonly RedisScript Read = new("atomic.read", """
        local entry = redis.call('HMGET', KEYS[1], 'v', 'ver')
        if not entry[1] then return nil end
        return {tonumber(entry[1]), tonumber(entry[2])}
        """, writes: false);

    /// <summary>
    /// Acquires the lease KEYS[1] for holder ARGV[1] for ARGV[2] ms, with a token from INCR on KEYS[2]. Returns
    /// {1, token, pttl} when acquired and {0, 0, pttl} when another holder has it.
    /// </summary>
    public static readonly RedisScript Acquire = new("leases.acquire", """
        if redis.call('GET', KEYS[1]) then return {0, 0, redis.call('PTTL', KEYS[1])} end
        local token = redis.call('INCR', KEYS[2])
        redis.call('SET', KEYS[1], ARGV[1] .. ':' .. token, 'NX', 'PX', ARGV[2])
        return {1, token, redis.call('PTTL', KEYS[1])}
        """, writes: true);

    /// <summary>Extends the lease KEYS[1] to ARGV[2] ms if it still holds ARGV[1] (holder:token). Returns {1, pttl}, or {0, 0} when lost.</summary>
    public static readonly RedisScript Renew = new("leases.renew", """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return {0, 0} end
        redis.call('SET', KEYS[1], ARGV[1], 'XX', 'PX', ARGV[2])
        return {1, redis.call('PTTL', KEYS[1])}
        """, writes: true);

    /// <summary>Deletes the lease KEYS[1] if it still holds ARGV[1] (holder:token). Returns 1 when deleted and 0 otherwise.</summary>
    public static readonly RedisScript ReleaseLease = new("leases.release", """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
        redis.call('DEL', KEYS[1])
        return 1
        """, writes: true);

    /// <summary>
    /// Deletes the lease KEYS[1] if it holds holder ARGV[1] with any token, undoing an acquire whose reply was abandoned.
    /// Returns 1 when deleted and 0 otherwise. Sent inline, so it follows the acquire on the connection with nothing between.
    /// </summary>
    public static readonly RedisScript ReleaseHolder = new("leases.release_holder", """
        local v = redis.call('GET', KEYS[1])
        if not v or string.sub(v, 1, string.len(ARGV[1]) + 1) ~= ARGV[1] .. ':' then return 0 end
        redis.call('DEL', KEYS[1])
        return 1
        """, writes: true, inline: true);

    /// <summary>
    /// Reads the entry KEYS[1] for version ARGV[1] under the generation at KEYS[2]. Returns {1, value} on a hit, and
    /// {0, generation} on a miss, so the miss carries the generation it observed.
    /// </summary>
    public static readonly RedisScript CacheGet = new("cache.get", """
        local generation = redis.call('GET', KEYS[2]) or '0'
        local entry = redis.call('HMGET', KEYS[1], 'ver', 'gen', 'val')
        if entry[1] == ARGV[1] and entry[2] == generation and entry[3] then return {1, entry[3]} end
        return {0, tonumber(generation)}
        """, writes: false);

    /// <summary>
    /// Stores the entry KEYS[1] with version ARGV[1] and value ARGV[2] for ARGV[3] ms, only if the generation at KEYS[2] is
    /// still ARGV[4]. Returns 1 when stored and 0 otherwise.
    /// </summary>
    public static readonly RedisScript CacheSet = new("cache.set", """
        local generation = redis.call('GET', KEYS[2]) or '0'
        if generation ~= ARGV[4] then return 0 end
        redis.call('HSET', KEYS[1], 'ver', ARGV[1], 'gen', generation, 'val', ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        return 1
        """, writes: true);

    /// <summary>Bumps the namespace generation at KEYS[1], which never expires. Returns the new generation.</summary>
    public static readonly RedisScript CacheInvalidate = new("cache.invalidate", """
        return redis.call('INCR', KEYS[1])
        """, writes: true);

    /// <summary>Every script, in a fixed order.</summary>
    public static IReadOnlyList<RedisScript> All { get; } =
        [Add, Reserve, Release, CompareAndSet, Read, Acquire, Renew, ReleaseLease, ReleaseHolder, CacheGet, CacheSet, CacheInvalidate];

    /// <summary>The value key of <paramref name="name"/> in <paramref name="scope"/>: a hash <c>{v, ver}</c>.</summary>
    public static RedisKey ValueKey(CoordinationScope scope, CoordinationName name) => Key(name.Module, Hash(scope, name.Module, name.Name), "v");

    /// <summary>The lease key of <paramref name="name"/> in <paramref name="scope"/>: <c>holder:token</c>.</summary>
    public static RedisKey LeaseKey(CoordinationScope scope, CoordinationName name) => Key(name.Module, Hash(scope, name.Module, name.Name), "l");

    /// <summary>The token counter of <paramref name="name"/> in <paramref name="scope"/>, which never expires.</summary>
    public static RedisKey TokenKey(CoordinationScope scope, CoordinationName name) => Key(name.Module, Hash(scope, name.Module, name.Name), "t");

    /// <summary>The generation key of <paramref name="cacheNamespace"/> in <paramref name="scope"/>, which never expires.</summary>
    public static RedisKey GenerationKey(CoordinationScope scope, CacheNamespace cacheNamespace) =>
        Key(cacheNamespace.Module, Hash(scope, cacheNamespace.Module, cacheNamespace.Name), "g");

    /// <summary>
    /// The entry key of <paramref name="name"/> under <paramref name="cacheNamespace"/>: a hash <c>{ver, gen, val}</c> under
    /// the namespace's hash tag, so the set script reads the generation and writes the entry atomically.
    /// </summary>
    public static RedisKey EntryKey(CoordinationScope scope, CacheNamespace cacheNamespace, string name) =>
        Key(cacheNamespace.Module, Hash(scope, cacheNamespace.Module, cacheNamespace.Name), "e:" + Hash(scope, cacheNamespace.Module, name));

    /// <summary>
    /// <c>isk:v1:&lt;module&gt;:{&lt;hash&gt;}:&lt;suffix&gt;</c>, the only key shape the adapter writes. The suffix is
    /// <c>v</c>, <c>l</c>, <c>t</c>, <c>g</c>, or <c>e:&lt;hash&gt;</c>; anything else fails with <c>core.value.invalid</c>.
    /// </summary>
    public static RedisKey Key(string module, string hash, string suffix)
    {
        var key = $"isk:v1:{module}:{{{hash}}}:{suffix}";
        return KeyPattern().IsMatch(key) ? key : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    /// <summary>Whether <paramref name="key"/> has the adapter's key shape.</summary>
    public static bool IsAdapterKey(string key) => key is not null && KeyPattern().IsMatch(key);

    private static string Hash(CoordinationScope scope, string module, string name) =>
        CoordinationKey.Hash((scope ?? throw CoordinationFailures.Raise(ErrorCodes.CoordinationScopeRequired, CoordinationFailureClass.ScopeViolation)).Owner, module, name);

    [GeneratedRegex(@"\Aisk:v1:[a-z][a-z0-9_]*:\{[0-9a-f]{64}\}:(v|l|t|g|e:[0-9a-f]{64})\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

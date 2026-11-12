using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Connections;

/// <summary>
/// Runs alone: <c>SCRIPT FLUSH</c> empties the server's script cache for every client, and a class running beside it
/// could load a script again between the flush and the call that must see <c>NOSCRIPT</c>.
/// </summary>
[Collection(ScriptCacheTests.Name)]
public class RedisScriptsTests(RedisFixture fixture)
{
    private const string Lifetime = "60000";

    [Fact]
    public async Task AScriptFlushedWithScriptFlushIsReRunThroughTheNoScriptFallback()
    {
        fixture.RequireRedis();
        await using var connection = new RedisConnection(TestCoordinationSettings.For(fixture), TestCoordinationCredentialProvider.For(fixture));
        var name = new CoordinationName("fixture", "flushed_" + Guid.NewGuid().ToString("N"));
        var key = RedisScripts.ValueKey(CoordinationScope.Operator, name);

        Assert.Equal(new long[] { 1, 2, 1 }, (long[])(await Run(connection, RedisScripts.Add, [key], [2, Lifetime]))!);
        var hash = connection.LoadedHash(RedisScripts.Add);
        Assert.Matches("^[0-9a-f]{40}$", hash);

        var admin = await fixture.AdminAsync();
        await admin.ScriptFlushAsync();
        Assert.False(await admin.ScriptExistsAsync(RedisScripts.Add.Text));

        // The connection still holds the old hash, so this call gets NOSCRIPT and falls back to EVAL.
        Assert.Equal(new long[] { 1, 5, 2 }, (long[])(await Run(connection, RedisScripts.Add, [key], [3, Lifetime]))!);
        Assert.Null(connection.LoadedHash(RedisScripts.Add));

        // The next call loads the script again.
        Assert.Equal(new long[] { 1, 6, 3 }, (long[])(await Run(connection, RedisScripts.Add, [key], [1, Lifetime]))!);
        Assert.Equal(hash, connection.LoadedHash(RedisScripts.Add));
    }

    [Fact]
    public async Task EveryKeyTheAdapterWritesMatchesTheKeyPattern()
    {
        fixture.RequireRedis();
        var settings = TestCoordinationSettings.For(fixture);
        await using var connection = new RedisConnection(settings, TestCoordinationCredentialProvider.For(fixture));
        var scope = CoordinationScope.Operator;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var value = new CoordinationName("fixture", "value_" + suffix);
        var lease = new CoordinationName("fixture", "lease_" + suffix);
        var cacheNamespace = new CacheNamespace("fixture", "ns_" + suffix);
        RedisKey valueKey = RedisScripts.ValueKey(scope, value), leaseKey = RedisScripts.LeaseKey(scope, lease), tokenKey = RedisScripts.TokenKey(scope, lease);
        RedisKey generationKey = RedisScripts.GenerationKey(scope, cacheNamespace), entryKey = RedisScripts.EntryKey(scope, cacheNamespace, "entry");

        await Run(connection, RedisScripts.Add, [valueKey], [4, Lifetime]);
        await Run(connection, RedisScripts.Reserve, [valueKey], [1, 10, Lifetime]);
        await Run(connection, RedisScripts.Release, [valueKey], [1]);
        await Run(connection, RedisScripts.CompareAndSet, [valueKey], [3, 9, Lifetime]);
        await Run(connection, RedisScripts.Read, [valueKey], []);
        var acquired = (long[])(await Run(connection, RedisScripts.Acquire, [leaseKey, tokenKey], ["holder", Lifetime]))!;
        Assert.Equal(1L, acquired[0]);
        var held = "holder:" + acquired[1].ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1L, ((long[])(await Run(connection, RedisScripts.Renew, [leaseKey], [held, Lifetime]))!)[0]);
        await Run(connection, RedisScripts.CacheInvalidate, [generationKey], []);
        Assert.Equal(1L, (long)(await Run(connection, RedisScripts.CacheSet, [entryKey, generationKey], ["1", "payload", Lifetime, "1"])));
        Assert.Equal("payload", (string?)(await Run(connection, RedisScripts.CacheGet, [entryKey, generationKey], ["1"]))[1]);
        Assert.Equal(1L, (long)(await Run(connection, RedisScripts.ReleaseLease, [leaseKey], [held])));

        var admin = await fixture.AdminAsync();
        var written = new List<string>();
        await foreach (var key in admin.KeysAsync(pattern: "*"))
        {
            written.Add(key.ToString());
        }

        // The released lease key is gone; the value, token counter, generation, and entry remain.
        Assert.Superset(new[] { valueKey, tokenKey, generationKey, entryKey }.Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal), written.ToHashSet(StringComparer.Ordinal));
        Assert.DoesNotContain(leaseKey.ToString(), written);
        Assert.All(written, key => Assert.True(RedisScripts.IsAdapterKey(key), $"A key outside the pattern was written: {key}"));
        Assert.All(RedisScripts.All, script => Assert.Matches("^(atomic|leases|cache)\\.[a-z_]+$", script.Name));
    }

    [Theory]
    [InlineData("fixture", "v")]
    [InlineData("fixture", "l")]
    [InlineData("fixture", "t")]
    [InlineData("fixture", "g")]
    [InlineData("a_b2", "e:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void AKeyHasTheAdapterShape(string module, string suffix)
    {
        var key = RedisScripts.Key(module, new string('a', 64), suffix).ToString();

        Assert.Equal($"isk:v1:{module}:{{{new string('a', 64)}}}:{suffix}", key);
        Assert.True(RedisScripts.IsAdapterKey(key));
    }

    [Theory]
    [InlineData("Fixture", "v")]
    [InlineData("1fixture", "v")]
    [InlineData("fixture", "x")]
    [InlineData("fixture", "e:short")]
    [InlineData("fixture", "v:extra")]
    [InlineData("fix:ture", "v")]
    public void AnyOtherKeyShapeFailsWithValueInvalid(string module, string suffix)
    {
        var failure = Assert.Throws<GatewayException>(() => RedisScripts.Key(module, new string('a', 64), suffix));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.False(RedisScripts.IsAdapterKey($"isk:v1:{module}:{{{new string('a', 64)}}}:{suffix}"));
    }

    [Fact]
    public void ScopesAndNamesGiveDistinctKeys()
    {
        var name = new CoordinationName("fixture", "counter");

        Assert.NotEqual(RedisScripts.ValueKey(CoordinationScope.Operator, name), RedisScripts.ValueKey(CoordinationScope.Operator, new CoordinationName("fixture", "counter2")));
        Assert.Equal(RedisScripts.ValueKey(CoordinationScope.Operator, name), RedisScripts.ValueKey(CoordinationScope.Operator, name));
        Assert.Throws<GatewayException>(() => RedisScripts.ValueKey(null!, name));
    }

    private static async Task<RedisResult> Run(RedisConnection connection, RedisScript script, RedisKey[] keys, RedisValue[] arguments) =>
        await connection.RunAsync(CoordinationScope.Operator, "fixture", CoordinationPrimitive.AtomicOperations, script, keys, arguments, CancellationToken.None);
}

/// <summary>Tests that flush the server's script cache, run with no other class in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScriptCacheTests
{
    public const string Name = "Redis script cache";
}

using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Leases;

public sealed partial class RedisLeaseTests(RedisFixture fixture) : IAsyncDisposable
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);
    private readonly string _module = "rl_" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItDeclaresLeasedLeasesAndDoesNotOfferFencing()
    {
        Assert.Equal(CoordinatorKind.Redis, RedisLeaseCoordinator.Capabilities.Kind);
        Assert.Equal(ConsistencyLevel.Leased, RedisLeaseCoordinator.Capabilities.LevelOf(CoordinationPrimitive.Leases));
        Assert.Null(RedisLeaseCoordinator.Capabilities.LevelOf(CoordinationPrimitive.Fencing));
    }

    [Fact]
    public async Task TheLeaseAndTokenKeysShareOneHashTagAndMatchTheKeyPattern()
    {
        fixture.RequireRedis();
        var name = new CoordinationName(_module, "lease");
        var lease = (await _coordinator.Leases(0).AcquireAsync(_coordinator.Scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;

        var keys = await KeysAsync();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.True(RedisScripts.IsAdapterKey(key), key));
        Assert.Single(keys.Select(key => HashTag().Match(key).Value).Distinct());
        Assert.Equal(["l", "t"], keys.Select(key => key[(key.LastIndexOf(':') + 1)..]).Order());

        var admin = await fixture.AdminDatabaseAsync();
        var stored = (string?)await admin.StringGetAsync(keys.Single(key => key.EndsWith(":l", StringComparison.Ordinal)));
        Assert.Equal(lease.Holder.ToString("N") + ":" + lease.Token.Value, stored);
        await _coordinator.Leases(0).ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task TheKeySpaceHoldsNoOrganizationProjectOrNameText()
    {
        fixture.RequireRedis();
        var scopes = _coordinator.Scopes;
        var leaseName = "lease_name_" + Guid.NewGuid().ToString("N")[..8];
        foreach (var scope in new[] { scopes.OrganizationA, scopes.ProjectA })
        {
            await _coordinator.Leases(0).AcquireAsync(scope, new CoordinationName(_module, leaseName), Duration, LeaseWait.TryOnly, Token);
        }

        var keys = await KeysAsync();
        Assert.Equal(4, keys.Count);
        var forbidden = new[]
        {
            scopes.OrganizationAScope.Organization.ToString(),
            scopes.ProjectAScope.Organization.ToString(),
            scopes.ProjectAScope.Project.ToString(),
            leaseName,
        };
        Assert.All(keys, key => Assert.All(forbidden, text => Assert.DoesNotContain(text, key, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task TokensIncreaseStrictlyOnOneKeyAcrossClients()
    {
        fixture.RequireRedis();
        var name = new CoordinationName(_module, "lease");
        var tokens = new List<long>();
        for (var index = 0; index < 6; index++)
        {
            var leases = _coordinator.Leases(index % 2);
            var lease = (await leases.AcquireAsync(_coordinator.Scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
            tokens.Add(lease.Token.Value);
            await leases.ReleaseAsync(lease, Token);
        }

        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public async Task ALeaseFromAnotherCoordinatorIsRefused()
    {
        var foreign = Lease.Grant(_coordinator.Scopes.OrganizationA, new CoordinationName(_module, "foreign"), new FencingToken(1), Guid.NewGuid(), CoordinatorKind.PostgreSql, Duration, TimeProvider.System.GetTimestamp(), TimeSpan.Zero, TimeProvider.System);
        fixture.RequireRedis();
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await _coordinator.Leases(0).RenewAsync(foreign, Duration, Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await _coordinator.Leases(0).ReleaseAsync(foreign, Token))).Code);
    }

    [Fact]
    public async Task CancellationWhileAnAcquireIsInFlightLeavesNoLeaseHeld()
    {
        // A container of its own, so pausing it leaves every other test's server alone.
        await using var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        var settings = TestCoordinationSettings.For(redis, sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "10s" });
        await using var first = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        await using var second = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        var (a, b) = (new RedisLeaseCoordinator(first), new RedisLeaseCoordinator(second));
        var scope = _coordinator.Scopes.OrganizationA;
        var name = new CoordinationName(_module, "in_flight");

        // Connected and the acquire script loaded, so the acquire under test is one EVALSHA and nothing else.
        await a.ReleaseAsync((await a.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!, Token);

        // Connected too, so the second client's acquire afterwards waits for nothing.
        await b.ReleaseAsync((await b.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!, Token);

        Task<LeaseAcquisition> acquiring;
        await redis.PauseAsync();
        try
        {
            // A lease far longer than the test, so only the release can free it. The command is written to the socket,
            // and the server takes it only once it resumes, after the caller has stopped waiting.
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            acquiring = a.AcquireAsync(scope, name, TimeSpan.FromSeconds(60), LeaseWait.TryOnly, cancellation.Token).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
        }
        finally
        {
            await redis.UnpauseAsync();
        }

        var failure = await Assert.ThrowsAsync<GatewayException>(() => acquiring.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
        var other = await b.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token);
        Assert.True(other.IsAcquired);
        await b.ReleaseAsync(other.Lease!, Token);
    }

    [Fact]
    public async Task CancellationDuringScriptLoadSendsNoScriptAndLeavesNoLeaseHeld()
    {
        // A container of its own, so pausing it leaves every other test's server alone, and its script counts are its own.
        await using var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        var settings = TestCoordinationSettings.For(redis, sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "10s" });
        await using var first = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        await using var second = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        var scope = _coordinator.Scopes.OrganizationA;
        var name = new CoordinationName(_module, "loading");

        // Both connected, and no script loaded, so the acquire under test starts with SCRIPT LOAD.
        await first.DatabaseAsync(Token);
        await second.DatabaseAsync(Token);

        Task<LeaseAcquisition> acquiring;
        await redis.PauseAsync();
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            acquiring = new RedisLeaseCoordinator(first).AcquireAsync(scope, name, TimeSpan.FromSeconds(60), LeaseWait.TryOnly, cancellation.Token).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
        }
        finally
        {
            await redis.UnpauseAsync();
        }

        var failure = await Assert.ThrowsAsync<GatewayException>(() => acquiring.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);

        // Nothing was sent, so the outcome is known.
        Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));

        // The SCRIPT LOAD the server took once it resumed was followed by no script call.
        await WaitForScriptLoadAsync(first);
        await Task.Delay(TimeSpan.FromSeconds(1), Token);
        Assert.Equal(0, await ScriptCallsAsync(redis));

        var other = await new RedisLeaseCoordinator(second).AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public async Task ALostScriptIsNotRunAgainOnceAnInFlightAcquireWasCancelled()
    {
        // A container of its own, so pausing it and flushing its scripts leaves every other test's server alone.
        await using var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        var settings = TestCoordinationSettings.For(redis, sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "10s" });
        await using var first = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        await using var second = new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis));
        var (a, b) = (new RedisLeaseCoordinator(first), new RedisLeaseCoordinator(second));
        var scope = _coordinator.Scopes.OrganizationA;
        var name = new CoordinationName(_module, "flushed");

        // The acquire script loaded, then lost by the server, so the acquire under test gets NOSCRIPT once it resumes.
        await a.ReleaseAsync((await a.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!, Token);
        await b.ReleaseAsync((await b.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!, Token);
        await (await redis.AdminAsync()).ExecuteAsync("SCRIPT", "FLUSH");

        Task<LeaseAcquisition> acquiring;
        await redis.PauseAsync();
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            acquiring = a.AcquireAsync(scope, name, TimeSpan.FromSeconds(60), LeaseWait.TryOnly, cancellation.Token).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(300), Token);
        }
        finally
        {
            await redis.UnpauseAsync();
        }

        var failure = await Assert.ThrowsAsync<GatewayException>(() => acquiring.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);

        // The NOSCRIPT reply was seen, and dropped the hash, without the fallback being sent.
        await WaitUntilAsync(() => first.LoadedHash(RedisScripts.Acquire) is null);
        await Task.Delay(TimeSpan.FromSeconds(1), Token);
        var other = await b.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public async Task ReleasingByHolderNeverTouchesAnotherHoldersLease()
    {
        fixture.RequireRedis();
        var name = new CoordinationName(_module, "holder");
        var scope = _coordinator.Scopes.OrganizationA;
        var lease = (await _coordinator.Leases(0).AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
        var key = RedisScripts.LeaseKey(scope, name);
        var connection = _coordinator.Connection(0);
        var holder = lease.Holder.ToString("N");

        // Another holder, and a holder that is a prefix of this one, both leave it held.
        foreach (var other in new[] { Guid.NewGuid().ToString("N"), holder[..31] })
        {
            Assert.Equal(0, (long)await connection.RunAsync(scope, _module, CoordinationPrimitive.Leases, RedisScripts.ReleaseHolder, [key], [other], Token));
        }

        Assert.True(await (await fixture.AdminDatabaseAsync()).KeyExistsAsync(key));
        Assert.Equal(1, (long)await connection.RunAsync(scope, _module, CoordinationPrimitive.Leases, RedisScripts.ReleaseHolder, [key], [holder], Token));
        Assert.False(await (await fixture.AdminDatabaseAsync()).KeyExistsAsync(key));
    }

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();

    private async Task<List<string>> KeysAsync()
    {
        var server = await fixture.AdminAsync();
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: $"isk:v1:{_module}:*"))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

    // EVALSHA and EVAL calls the server has run, from INFO commandstats.
    private static async Task<long> ScriptCallsAsync(RedisFixture redis)
    {
        var server = await redis.AdminAsync();
        var stats = (await server.InfoAsync("commandstats")).SelectMany(group => group);
        return stats.Where(stat => stat.Key is "cmdstat_evalsha" or "cmdstat_eval")
            .Sum(stat => long.Parse(stat.Value.Split(',')[0]["calls=".Length..], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Task WaitForScriptLoadAsync(RedisConnection connection) =>
        WaitUntilAsync(() => connection.LoadedHash(RedisScripts.Acquire) is not null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        Assert.True(condition());
    }

    [GeneratedRegex(@"\{[0-9a-f]{64}\}", RegexOptions.CultureInvariant)]
    private static partial Regex HashTag();
}

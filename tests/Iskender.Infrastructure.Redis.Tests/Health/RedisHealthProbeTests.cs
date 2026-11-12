using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Health;

/// <summary>
/// Redis's health probe (FR-025, SC-006, research R17): <c>ok</c>, <c>unreachable</c> with the server stopped,
/// <c>authentication_failed</c> with a wrong password and with no credential provider, <c>timeout</c> with the server
/// paused, and <c>read_only</c> with the server a replica, each within <c>healthProbeTimeout</c> plus the fixed
/// 2-second allowance, and the caller's token giving <c>coordination.cancelled</c>. A test that stops, pauses, or demotes
/// the server does it to a container of its own and puts it back before it is removed.
/// </summary>
public class RedisHealthProbeTests(RedisFixture fixture)
{
    private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(1);

    // The contract's fixed safety bound past healthProbeTimeout.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnsweringPrimaryIsOk()
    {
        fixture.RequireRedis();
        await using var connection = Connect(fixture, TestCoordinationCredentialProvider.For(fixture));

        Assert.Equal(CoordinationHealthResult.Healthy, await CheckWithinBoundAsync(new RedisHealthProbe(connection)));
    }

    [Fact]
    public async Task AStoppedServerIsUnreachable()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, TestCoordinationCredentialProvider.For(redis));
        var probe = new RedisHealthProbe(connection);
        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));

        await redis.StopAsync();
        try
        {
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable), await CheckWithinBoundAsync(probe));
        }
        finally
        {
            await redis.StartAsync();
        }
    }

    [Fact]
    public async Task AWrongPasswordIsAuthenticationFailed()
    {
        fixture.RequireRedis();
        var credentials = TestCoordinationCredentialProvider.For(fixture);
        credentials.Override = "not-the-password";
        await using var connection = Connect(fixture, credentials);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed), await CheckWithinBoundAsync(new RedisHealthProbe(connection)));
    }

    [Fact]
    public async Task NoCredentialProviderIsAuthenticationFailed()
    {
        fixture.RequireRedis();
        await using var connection = Connect(fixture, credentials: null);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed), await CheckWithinBoundAsync(new RedisHealthProbe(connection)));
    }

    [Fact]
    public async Task APausedServerIsATimeout()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, TestCoordinationCredentialProvider.For(redis));
        var probe = new RedisHealthProbe(connection);
        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));

        await redis.PauseAsync();
        try
        {
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout), await CheckWithinBoundAsync(probe));
        }
        finally
        {
            await redis.UnpauseAsync();
        }
    }

    [Fact]
    public async Task AReplicaIsReadOnly()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, TestCoordinationCredentialProvider.For(redis));

        await redis.BecomeReplicaAsync();
        try
        {
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly), await CheckWithinBoundAsync(new RedisHealthProbe(connection)));
        }
        finally
        {
            await redis.BecomePrimaryAsync();
        }
    }

    [Fact]
    public async Task TheCallersTokenGivesCancelled()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, TestCoordinationCredentialProvider.For(redis));
        var probe = new RedisHealthProbe(connection);
        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await redis.PauseAsync();
        try
        {
            var check = probe.CheckAsync(caller.Token).AsTask();
            await caller.CancelAsync();
            var failure = await Assert.ThrowsAsync<GatewayException>(() => check);
            Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
        }
        finally
        {
            await redis.UnpauseAsync();
        }
    }

    private static async Task<CoordinationHealthResult> CheckWithinBoundAsync(RedisHealthProbe probe)
    {
        var watch = Stopwatch.StartNew();
        var result = await probe.CheckAsync(Token);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, HealthProbeTimeout + Allowance);
        return result;
    }

    private static async Task<RedisFixture> StartAsync()
    {
        var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        return redis;
    }

    private static RedisConnection Connect(RedisFixture redis, TestCoordinationCredentialProvider? credentials)
    {
        var settings = TestCoordinationSettings.For(
            redis,
            redisOverrides: new Dictionary<string, object> { ["connectTimeout"] = "1s" },
            sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "1s", ["healthProbeTimeout"] = "1s" });
        return new RedisConnection(settings, credentials, new CapturingDiagnostics.Logger());
    }
}

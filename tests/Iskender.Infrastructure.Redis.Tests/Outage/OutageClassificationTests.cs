using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Outage;

/// <summary>
/// User Story 3 scenarios 4 and 5 and the outage edge cases against Redis (FR-022): an outage is classified within the
/// operation bound, never reported as granted, and never retried. Each test stops, pauses, or demotes a container of its
/// own, never the shared one, and puts it back before it is removed.
/// </summary>
public class OutageClassificationTests
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(1);

    // Covers scheduling and the client noticing a closed socket; well under any wait a retry would add.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly ConformanceScopes _scopes = new();
    private readonly CoordinationName _name = new("outage", "counter_" + Guid.NewGuid().ToString("N")[..8]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario4AStoppedServerFailsAReservationWithUnavailableWithinTheBound()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, out var logger);
        var atomic = new RedisAtomicOperations(connection);
        await atomic.AddAsync(_scopes.OrganizationA, _name, 1, Expiry, Token);

        await redis.StopAsync();
        try
        {
            var (failure, elapsed) = await FailAsync(() => atomic.ReserveAsync(_scopes.OrganizationA, _name, 1, 100, Expiry, Token));

            Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
            Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(failure));
            Assert.InRange(elapsed, TimeSpan.Zero, OperationTimeout + Allowance);
            Assert.Contains(logger.Events, logged => logged.Id.Id == 5003);
        }
        finally
        {
            await redis.StartAsync();
        }
    }

    [Fact]
    public async Task APausedServerFailsWithTimeoutWithinTheBound()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, out _);
        var atomic = new RedisAtomicOperations(connection);
        await atomic.AddAsync(_scopes.OrganizationA, _name, 1, Expiry, Token);

        await redis.PauseAsync();
        try
        {
            var (failure, elapsed) = await FailAsync(() => atomic.ReadAsync(_scopes.OrganizationA, _name, Token));

            Assert.Same(ErrorCodes.CoordinationTimeout, failure.Code);
            Assert.Equal(CoordinationFailureClass.Timeout, CoordinationFailures.ClassOf(failure));
            Assert.InRange(elapsed, OperationTimeout - TimeSpan.FromMilliseconds(50), OperationTimeout + Allowance);

            // A read changes nothing, so its outcome is never unknown.
            Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));
        }
        finally
        {
            await redis.UnpauseAsync();
        }
    }

    [Fact]
    public async Task AServerSwitchedToAReplicaFailsAWriteWithUnavailable()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, out _);
        var atomic = new RedisAtomicOperations(connection);
        await atomic.AddAsync(_scopes.OrganizationA, _name, 1, Expiry, Token);

        await redis.BecomeReplicaAsync();
        try
        {
            var (failure, _) = await FailAsync(() => atomic.AddAsync(_scopes.OrganizationA, _name, 1, Expiry, Token));

            Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
            Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(failure));
        }
        finally
        {
            await redis.BecomePrimaryAsync();
        }
    }

    [Fact]
    public async Task Scenario5AReservationSentBeforeTheServerStallsIsAnUnknownOutcomeAndIsNeverRetried()
    {
        await using var redis = await StartAsync();
        await using var connection = Connect(redis, out var logger);
        var atomic = new RedisAtomicOperations(connection);

        // Load the script first, so the reservation under test is one EVALSHA and nothing else.
        await atomic.ReserveAsync(_scopes.OrganizationA, _name, 1, 100, Expiry, Token);
        var before = await ScriptCallsAsync(redis);

        await redis.PauseAsync();
        GatewayException failure;
        try
        {
            // The command is written to the socket; the server takes it only once it resumes.
            (failure, _) = await FailAsync(() => atomic.ReserveAsync(_scopes.OrganizationA, _name, 1, 100, Expiry, Token));
        }
        finally
        {
            await redis.UnpauseAsync();
        }

        Assert.Same(ErrorCodes.CoordinationTimeout, failure.Code);
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));
        Assert.Contains(logger.Events, logged => logged.Id.Id == 5004);

        // The resumed server ran the reservation it had received, exactly once: the adapter sent it once and never again.
        var after = await WaitForCallsAsync(redis, before + 1);
        Assert.Equal(before + 1, after);
        Assert.Equal(new CoordinationValue(2, 2), await atomic.ReadAsync(_scopes.OrganizationA, _name, Token));
    }

    private static async Task<RedisFixture> StartAsync()
    {
        var redis = new RedisFixture();
        await redis.InitializeAsync();
        redis.RequireRedis();
        return redis;
    }

    private static RedisConnection Connect(RedisFixture redis, out CapturingDiagnostics.Logger logger)
    {
        logger = new CapturingDiagnostics.Logger();
        var settings = TestCoordinationSettings.For(
            redis,
            redisOverrides: new Dictionary<string, object> { ["connectTimeout"] = "1s" },
            sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "1s" });
        return new RedisConnection(settings, TestCoordinationCredentialProvider.For(redis), logger);
    }

    private static async Task<(GatewayException Failure, TimeSpan Elapsed)> FailAsync<T>(Func<ValueTask<T>> call)
    {
        var watch = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await call());
        return (failure, watch.Elapsed);
    }

    // EVALSHA and EVAL calls the server has run, from INFO commandstats.
    private static async Task<long> ScriptCallsAsync(RedisFixture redis)
    {
        var server = await redis.AdminAsync();
        var stats = (await server.InfoAsync("commandstats")).SelectMany(group => group);
        return stats.Where(stat => stat.Key is "cmdstat_evalsha" or "cmdstat_eval")
            .Sum(stat => long.Parse(stat.Value.Split(',')[0]["calls=".Length..], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<long> WaitForCallsAsync(RedisFixture redis, long expected)
    {
        var calls = await ScriptCallsAsync(redis);
        for (var attempt = 0; attempt < 20 && calls < expected; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
            calls = await ScriptCallsAsync(redis);
        }

        // Give a retry, if there were one, time to arrive before the count is read for the last time.
        await Task.Delay(OperationTimeout, Token);
        return await ScriptCallsAsync(redis);
    }
}

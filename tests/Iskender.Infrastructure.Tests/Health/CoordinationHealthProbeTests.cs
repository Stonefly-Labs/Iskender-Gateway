using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Health;

/// <summary>
/// The composed coordination health probe (FR-025, contract Health, research R17), over stand-in probes: which coordinators
/// it checks and in what order, both <c>not_configured</c> results, a probe that overruns or fails unclassified, the
/// caller's token, and event 5009 only on a change.
/// </summary>
public class CoordinationHealthProbeTests
{
    private const string RedisEverywhere =
        "\"atomic\":{\"coordinator\":\"redis\"},\"leases\":{\"coordinator\":\"redis\"},\"cache\":{\"coordinator\":\"redis\"},\"redis\":{\"endpoints\":[\"cache.internal:6379\"]}";

    private const string Both =
        "\"atomic\":{\"coordinator\":\"redis\"},\"leases\":{\"coordinator\":\"postgresql\"},\"redis\":{\"endpoints\":[\"cache.internal:6379\"]}";

    private readonly CapturingDiagnostics.Logger _logger = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NothingDistributedIsHealthyWithNotConfigured()
    {
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(), postgreSqlConfigured: false, redis: null, postgreSql: null, _logger);

        Assert.Equal(CoordinationHealthResult.NothingDistributed, await probe.CheckAsync(Token));
    }

    [Fact]
    public async Task APrimitiveOnRedisWithoutEndpointsIsUnhealthyWithNotConfigured()
    {
        var settings = LocalCoordinatorUnderTest.ReadSettings("\"atomic\":{\"coordinator\":\"redis\"}");
        var probe = new CoordinationHealthProbe(settings, postgreSqlConfigured: true, redis: Answering.Healthy, postgreSql: null, _logger);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.NotConfigured), await probe.CheckAsync(Token));
    }

    [Fact]
    public async Task APrimitiveOnPostgreSqlWithoutPersistenceIsUnhealthyWithNotConfigured()
    {
        var settings = LocalCoordinatorUnderTest.ReadSettings("\"leases\":{\"coordinator\":\"postgresql\"}");
        var probe = new CoordinationHealthProbe(settings, postgreSqlConfigured: false, redis: null, postgreSql: Answering.Healthy, _logger);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.NotConfigured), await probe.CheckAsync(Token));
    }

    [Fact]
    public async Task EveryCoordinatorAnsweringIsHealthyWithOk()
    {
        var redis = Answering.Healthy;
        var postgreSql = Answering.Healthy;
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(Both), postgreSqlConfigured: true, redis, postgreSql, _logger);

        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));
        Assert.Equal(1, redis.Calls);
        Assert.Equal(1, postgreSql.Calls);
    }

    [Fact]
    public async Task RedisIsCheckedFirstAndItsUnhealthyResultIsReported()
    {
        var postgreSql = new Answering(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable));
        var probe = new CoordinationHealthProbe(
            LocalCoordinatorUnderTest.ReadSettings(Both),
            postgreSqlConfigured: true,
            new Answering(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly)),
            postgreSql,
            _logger);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly), await probe.CheckAsync(Token));
        Assert.Equal(0, postgreSql.Calls);
    }

    [Fact]
    public async Task OnlyTheCoordinatorsInUseAreChecked()
    {
        var postgreSql = Answering.Healthy;
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(RedisEverywhere), postgreSqlConfigured: true, Answering.Healthy, postgreSql, _logger);

        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));
        Assert.Equal(0, postgreSql.Calls);
    }

    [Fact]
    public async Task AProbeThatFailsUnclassifiedIsUnexpectedWithEvent5010()
    {
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(RedisEverywhere), postgreSqlConfigured: false, new Throwing(), null, _logger);

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unexpected), await probe.CheckAsync(Token));
        Assert.Contains(_logger.Events, logged => logged.Id.Id == 5010);
    }

    [Fact]
    public async Task AProbeThatOverrunsHealthProbeTimeoutIsATimeout()
    {
        var clock = new ManualTimeProvider();
        var settings = LocalCoordinatorUnderTest.ReadSettings(RedisEverywhere);
        var probe = new CoordinationHealthProbe(settings, postgreSqlConfigured: false, new Hanging(), null, _logger, clock);

        var check = probe.CheckAsync(Token).AsTask();
        clock.Advance(settings.HealthProbeTimeout - TimeSpan.FromMilliseconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(20), Token);
        Assert.False(check.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout), await check.WaitAsync(TimeSpan.FromSeconds(5), Token));
    }

    [Fact]
    public async Task TheCallersTokenGivesCancelled()
    {
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(RedisEverywhere), postgreSqlConfigured: false, new Hanging(), null, _logger);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var check = probe.CheckAsync(caller.Token).AsTask();
        await caller.CancelAsync();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => check);
        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
        Assert.Same(ErrorCodes.CoordinationCancelled, (await Assert.ThrowsAsync<GatewayException>(async () => await probe.CheckAsync(caller.Token))).Code);
    }

    [Fact]
    public async Task Event5009IsWrittenOnlyWhenTheResultChanges()
    {
        var redis = Answering.Healthy;
        var probe = new CoordinationHealthProbe(LocalCoordinatorUnderTest.ReadSettings(RedisEverywhere), postgreSqlConfigured: false, redis, null, _logger);

        await probe.CheckAsync(Token);
        await probe.CheckAsync(Token);
        Assert.Single(_logger.Events, logged => logged.Id.Id == 5009);

        redis.Result = CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout);
        await probe.CheckAsync(Token);
        await probe.CheckAsync(Token);
        Assert.Equal(2, _logger.Events.Count(logged => logged.Id.Id == 5009));

        redis.Result = CoordinationHealthResult.Healthy;
        await probe.CheckAsync(Token);
        Assert.Equal(3, _logger.Events.Count(logged => logged.Id.Id == 5009));
    }

    private sealed class Answering(CoordinationHealthResult result) : ICoordinationHealthProbe
    {
        public static Answering Healthy => new(CoordinationHealthResult.Healthy);

        public CoordinationHealthResult Result { get; set; } = result;

        public int Calls { get; private set; }

        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class Throwing : ICoordinationHealthProbe
    {
        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("unclassified");
    }

    private sealed class Hanging : ICoordinationHealthProbe
    {
        private readonly TaskCompletionSource<CoordinationHealthResult> _never = new();

        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken) => new(_never.Task.WaitAsync(cancellationToken));
    }
}

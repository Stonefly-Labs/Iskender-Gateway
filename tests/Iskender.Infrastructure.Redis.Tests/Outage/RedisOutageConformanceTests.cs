using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Outage;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Iskender.Infrastructure.Redis.Tests.Leases;

namespace Iskender.Infrastructure.Redis.Tests.Outage;

/// <summary>The outage suite against the Redis coordinator, on a container of its own, so stopping or pausing it leaves every other test's server alone.</summary>
public sealed class RedisOutageConformanceTests : OutageConformanceTests, IAsyncLifetime
{
    private readonly RedisFixture _redis = new();
    private RedisCoordinatorUnderTest? _coordinator;

    protected override ICoordinatorUnderTest Coordinator => _coordinator ?? throw new InvalidOperationException("The coordinator was not created.");

    public async ValueTask InitializeAsync()
    {
        await _redis.InitializeAsync();
        _coordinator = new RedisCoordinatorUnderTest(_redis, ownsFixture: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }
}

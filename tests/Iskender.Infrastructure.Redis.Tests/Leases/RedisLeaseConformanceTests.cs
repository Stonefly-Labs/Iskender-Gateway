using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Leases;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Leases;

/// <summary>The lease suite against <c>RedisLeaseCoordinator</c>, each client on a multiplexer of its own.</summary>
public sealed class RedisLeaseConformanceTests(RedisFixture fixture) : LeaseConformanceTests, IAsyncDisposable
{
    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}

/// <summary>The fencing suite with Redis leases, writing through <see cref="RecordingFence"/>.</summary>
public sealed class RedisFencingConformanceTests(RedisFixture fixture) : FencingConformanceTests, IAsyncDisposable
{
    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    protected override IFenceUnderTest Fence { get; } = new RecordingFence();

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}

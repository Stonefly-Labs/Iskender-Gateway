using Iskender.Coordination.Conformance;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Iskender.Infrastructure.Redis.Tests.Leases;

namespace Iskender.Infrastructure.Redis.Tests.Acceptance;

/// <summary>User Story 2 scenarios 1 to 4 against <c>RedisLeaseCoordinator</c>, writing through <see cref="RecordingFence"/>.</summary>
public sealed class LeaseFencingAcceptanceTests(RedisFixture fixture)
    : Iskender.Coordination.Conformance.Acceptance.LeaseFencingAcceptanceTests, IAsyncDisposable
{
    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    protected override IFenceUnderTest Fence { get; } = new RecordingFence();

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}

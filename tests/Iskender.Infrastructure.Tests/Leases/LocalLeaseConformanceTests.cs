using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Leases;

namespace Iskender.Infrastructure.Tests.Leases;

/// <summary>The lease suite against <c>LocalLeaseCoordinator</c> on a manual clock.</summary>
public class LocalLeaseConformanceTests : LeaseConformanceTests
{
    private readonly LocalCoordinatorUnderTest _coordinator = new();

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    // The manual clock has no granularity to cover.
    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(1);
}

/// <summary>The fencing suite against <c>LocalLeaseCoordinator</c>, writing through <see cref="RecordingFence"/>.</summary>
public class LocalFencingConformanceTests : FencingConformanceTests
{
    private readonly LocalCoordinatorUnderTest _coordinator = new();

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    protected override IFenceUnderTest Fence { get; } = new RecordingFence();

    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(1);
}

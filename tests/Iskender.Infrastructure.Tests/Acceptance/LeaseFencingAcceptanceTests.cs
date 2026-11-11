using Iskender.Coordination.Conformance;

namespace Iskender.Infrastructure.Tests.Acceptance;

/// <summary>User Story 2 scenarios 1 to 4 against <c>LocalLeaseCoordinator</c>, writing through <see cref="RecordingFence"/>.</summary>
public class LeaseFencingAcceptanceTests : Iskender.Coordination.Conformance.Acceptance.LeaseFencingAcceptanceTests
{
    private readonly LocalCoordinatorUnderTest _coordinator = new();

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    protected override IFenceUnderTest Fence { get; } = new RecordingFence();

    // The manual clock has no granularity to cover.
    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(1);
}

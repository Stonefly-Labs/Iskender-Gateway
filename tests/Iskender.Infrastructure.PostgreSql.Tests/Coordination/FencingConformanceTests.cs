using Iskender.Coordination.Conformance;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>The fencing suite with leases from <c>PostgreSqlLeaseCoordinator</c>, writing through <see cref="PostgreSqlFence"/>.</summary>
public class FencingConformanceTests(PostgreSqlCoordinatorUnderTest coordinator)
    : Iskender.Coordination.Conformance.Leases.FencingConformanceTests, IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    protected override ICoordinatorUnderTest Coordinator => coordinator;

    protected override IFenceUnderTest Fence { get; } = new PostgreSqlFence(coordinator);
}

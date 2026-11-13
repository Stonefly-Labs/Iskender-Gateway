using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Atomic;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Atomic;

/// <summary>The atomic operations suite against <c>LocalAtomicOperations</c> on a manual clock.</summary>
public class LocalAtomicConformanceTests : AtomicOperationsConformanceTests
{
    private readonly LocalCoordinatorUnderTest _coordinator = new();

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    // The manual clock has no granularity to cover.
    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(1);

    [Fact]
    public void ItDeclaresLocal() =>
        Assert.Equal(ConsistencyLevel.Local, LocalAtomicOperations.Capabilities.LevelOf(CoordinationPrimitive.AtomicOperations));
}

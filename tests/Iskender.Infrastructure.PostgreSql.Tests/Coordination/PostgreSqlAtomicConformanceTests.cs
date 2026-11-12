using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Atomic;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>The atomic operations suite against <c>PostgreSqlAtomicOperations</c>, each client on a data source of its own.</summary>
public class PostgreSqlAtomicConformanceTests(PostgreSqlCoordinatorUnderTest coordinator) : AtomicOperationsConformanceTests, IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    protected override ICoordinatorUnderTest Coordinator => coordinator;

    [Fact]
    public void ItDeclaresStrict() =>
        Assert.Equal(ConsistencyLevel.Strict, PostgreSqlAtomicOperations.Capabilities.LevelOf(CoordinationPrimitive.AtomicOperations));
}

using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Leases;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>The lease suite against <c>PostgreSqlLeaseCoordinator</c>, each client on a data source of its own.</summary>
public class PostgreSqlLeaseConformanceTests(PostgreSqlCoordinatorUnderTest coordinator) : LeaseConformanceTests, IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    protected override ICoordinatorUnderTest Coordinator => coordinator;
}

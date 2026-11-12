using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Cache;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The versioned cache suite against <c>PostgreSqlVersionedCache</c>: each client on a data source of its own with an
/// in-memory store of its own, so a bump on one makes the other's next read a miss.
/// </summary>
public class PostgreSqlCacheConformanceTests(PostgreSqlCoordinatorUnderTest coordinator) : VersionedCacheConformanceTests, IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    protected override ICoordinatorUnderTest Coordinator => coordinator;

    // Entries expire on this process's clock, so there is no server granularity to cover.
    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(20);

    [Fact]
    public void ItDeclaresStrict() =>
        Assert.Equal(ConsistencyLevel.Strict, PostgreSqlVersionedCache.Capabilities.LevelOf(CoordinationPrimitive.CacheInvalidation));
}

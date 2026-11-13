using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Cache;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Cache;

/// <summary>The versioned cache suite against <c>LocalVersionedCache</c> on a manual clock.</summary>
public class LocalCacheConformanceTests : VersionedCacheConformanceTests
{
    private readonly LocalCoordinatorUnderTest _coordinator = new();

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    // The manual clock has no granularity to cover.
    protected override TimeSpan Slack => TimeSpan.FromMilliseconds(1);

    [Fact]
    public void ItDeclaresLocal() =>
        Assert.Equal(ConsistencyLevel.Local, LocalVersionedCache.Capabilities.LevelOf(CoordinationPrimitive.CacheInvalidation));
}

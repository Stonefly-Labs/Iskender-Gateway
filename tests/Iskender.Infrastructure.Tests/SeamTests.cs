using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Tests;

public class SeamTests
{
    [Fact]
    public async Task TheConformanceLibraryIsReachable()
    {
        var scopes = new ConformanceScopes();
        var fence = new RecordingFence();
        var name = new CoordinationName("seam", "check");

        Assert.Empty(await fence.CommittedTokensAsync(scopes.OrganizationA, name, TestContext.Current.CancellationToken));
        Assert.Contains(Canary.Value, Canary.OrganizationId.Value, StringComparison.Ordinal);
        Assert.Equal(4, scopes.All.Count);
    }
}

using System.Net;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.3: liveness calls no probe, however often it is asked.</summary>
public class LivenessProbeCountTests
{
    [Fact]
    public async Task AThousandLivenessRequestsMakeNoProbeCall()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        for (var request = 0; request < 1000; request++)
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(0, host.Persistence.Calls);
        Assert.Equal(0, host.Coordination.Calls);
        Assert.Equal(0, host.SingleInstance.Reads);
        host.Stop();
        await run;
    }
}

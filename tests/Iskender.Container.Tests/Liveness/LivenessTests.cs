using System.Net;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Liveness;

/// <summary>Container contract, Liveness.</summary>
public class LivenessTests
{
    [Fact]
    public async Task LiveAnswers200WithItsBodyAndCallsNoProbe()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        using var response = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("""{"version":1,"status":"live"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, host.Persistence.Calls);
        Assert.Equal(0, host.Coordination.Calls);
        Assert.Equal(0, host.SingleInstance.Reads);

        host.Stop();
        await run;
    }

    [Fact]
    public async Task LiveAnswersWhileAProbeHangs()
    {
        using var host = new ContainerTestHost().WithDocument();
        host.Persistence.Hold();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        using var response = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        host.Persistence.Release();
        host.Stop();
        await run;
    }
}

using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Listeners;

/// <summary>Container contract, Listeners rules 1 to 5: requests are routed by the listener's port, never the <c>Host</c> header.</summary>
public class ListenerTests
{
    [Fact]
    public async Task BothListenersBindTheirConfiguredPortsAndAreToldApartByPort()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.WithTraffic(host));

        Assert.NotEqual(8080, host.TrafficPort);
        Assert.Equal(TestPlanes.TrafficAnswer, await client.GetStringAsync($"http://127.0.0.1:{host.TrafficPort}/hello", TestContext.Current.CancellationToken));
        Assert.Equal(TestPlanes.TrafficAnswer, await client.GetStringAsync($"http://127.0.0.1:{host.TrafficPort}/health/live", TestContext.Current.CancellationToken));
        Assert.Contains("\"live\"", await client.GetStringAsync($"http://127.0.0.1:{host.HealthPort}/health/live", TestContext.Current.CancellationToken), StringComparison.Ordinal);

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task AHostHeaderNamingTheHealthPortDoesNotReachTheHealthEndpoints()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.WithTraffic(host));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{host.TrafficPort}/health/live");
        request.Headers.Host = $"localhost:{host.HealthPort}";
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(TestPlanes.TrafficAnswer, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        host.Stop();
        await run;
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/hello")]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/health/live/")]
    [InlineData("GET", "/HEALTH/LIVE")]
    [InlineData("POST", "/health/live")]
    [InlineData("HEAD", "/health/live")]
    public async Task TheHealthListenerAnswersAnythingElseWith404AndNoBody(string method, string path)
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.WithTraffic(host));

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{host.HealthPort}{path}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        host.Stop();
        await run;
    }

    [Fact]
    public async Task TheControlPlaneBindsOnlyItsHealthPort()
    {
        using var host = new ContainerTestHost().WithDocument();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        Assert.True(await ContainerTestHost.IsListeningAsync(host.HealthPort));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }
}

using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>FR-011a and Listeners rule 6: readiness is advisory, so the traffic listener serves the same while not ready.</summary>
public class NotReadyTrafficTests
{
    [Fact]
    public async Task ARequestIsServedTheSameWayWhetherOrNotTheProcessIsReady()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.DataPlane(host));
        var (_, ready) = await host.WhenStartedAsync(client);
        Assert.Equal("ready", ready.GetProperty("status").GetString());
        var whenReady = await SendAsync(client, host);

        host.Persistence.Result = PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Unreachable);
        var (_, notReady) = await host.ReadyAsync(client);
        Assert.Equal("not_ready", notReady.GetProperty("status").GetString());
        var whenNotReady = await SendAsync(client, host);

        Assert.Equal(whenReady, whenNotReady);
        host.Stop();
        await run;
    }

    private static async Task<(System.Net.HttpStatusCode Status, string? ContentType, string? Code)> SendAsync(HttpClient client, ContainerTestHost host)
    {
        using var response = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}/v1/anything", TestContext.Current.CancellationToken);
        var code = TrafficListenerTests.Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, code);
    }
}

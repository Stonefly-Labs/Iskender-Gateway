using System.Net;
using System.Text.Json;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.5 and Listeners rules 2 and 4: the health paths on the traffic listener reach Iskender's route group.</summary>
public class TrafficListenerTests
{
    [Theory]
    [InlineData("/health/ready", false)]
    [InlineData("/health/live", false)]
    [InlineData("/health/ready", true)]
    [InlineData("/health/live", true)]
    public async Task AHealthPathOnTheTrafficPortGetsIskendersNotFoundEnvelope(string path, bool healthHostHeader)
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.DataPlane(host));
        await host.WhenStartedAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{host.TrafficPort}{path}");
        if (healthHostHeader)
        {
            request.Headers.Host = $"localhost:{host.HealthPort}";
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("core.resource.not_found", Code(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        host.Stop();
        await run;
    }

    internal static string? Code(string envelope)
    {
        using var document = JsonDocument.Parse(envelope);
        return document.RootElement.GetProperty("code").GetString();
    }
}

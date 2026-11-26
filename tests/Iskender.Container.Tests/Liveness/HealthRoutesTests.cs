using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container.Health;

namespace Iskender.Container.Tests.Liveness;

/// <summary>
/// The health listener's fixed route list (ISK-0010 T035, research R19, container contract Listeners rule 3): it is
/// exactly <c>GET /health/live</c> and <c>GET /health/ready</c>, the handler answers each listed route, and anything else
/// gets <c>404</c> with no body.
/// </summary>
public class HealthRoutesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheListIsExactlyTheTwoHealthRoutes() =>
        Assert.Equal([("GET", "/health/live"), ("GET", "/health/ready")], HealthEndpoints.Routes);

    [Fact]
    public async Task TheHandlerAnswersExactlyTheListedRoutesAnd404OtherwiseWithNoBody()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        foreach (var (method, route) in HealthEndpoints.Routes)
        {
            using var listed = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{host.HealthPort}{route}"), Token);
            Assert.NotEqual(HttpStatusCode.NotFound, listed.StatusCode);
        }

        var unlisted = new (string Method, string Route)[]
        {
            ("POST", "/health/live"),
            ("HEAD", "/health/ready"),
            ("GET", "/health"),
            ("GET", "/health/live/"),
            ("GET", "/HEALTH/LIVE"),
            ("GET", "/admin/v1/installation"),
            ("GET", "/"),
        };
        foreach (var (method, route) in unlisted)
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{host.HealthPort}{route}"), Token);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        }

        host.Stop();
        await run;
    }
}

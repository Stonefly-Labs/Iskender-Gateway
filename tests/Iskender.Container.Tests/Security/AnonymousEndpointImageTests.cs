using System.Net;
using System.Text;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.Security;

/// <summary>
/// Each entry of the anonymous endpoint inventory, probed on the built images (ISK-0010 T068, research R19, FR-020;
/// CONV-0010-010): the data plane, the control plane, and the control-plane image's <c>work</c> command run beside
/// PostgreSQL, and every listed endpoint answers without authentication on its listener's published port, as the
/// in-process inventory test finds it mapped. Before ISK-0021 no credential provider exists, so a redemption with a
/// malformed token gets the uniform failure, which needs no store.
/// </summary>
[Trait("Category", "Compose")]
public class AnonymousEndpointImageTests(ImageBuild build)
{
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    private static readonly IReadOnlyDictionary<string, string> Services = new Dictionary<string, string>
    {
        ["data-plane"] = Scenarios.Service("database-running", "data-plane"),
        ["control-plane"] = Scenarios.Service("database-running", "control-plane"),
        ["worker"] = Scenarios.Service("database-running", "worker"),
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryListedEndpointAnswersWithoutAuthenticationOnTheBuiltImages()
    {
        var inventory = await AnonymousEndpointInventoryTests.InventoryAsync();
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());
        await project.UpAsync([.. Services.Values]);
        foreach (var service in Services.Values)
        {
            await project.WhenAsync(service, "/health/live", (status, _) => status == HttpStatusCode.OK, Settle);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var answers = new List<string>();
        foreach (var row in inventory)
        {
            var port = await project.PortAsync(Services[row.Process], row.Listener == "health" ? ComposeProject.HealthPort : ComposeProject.TrafficPort);
            using var request = Request(row, port);
            using var response = await client.SendAsync(request, Token);
            var body = await response.Content.ReadAsStringAsync(Token);
            answers.Add($"{row.Entry} {(int)response.StatusCode} {Expected(row, response.StatusCode, body)}");
        }

        Assert.Equal(inventory.Select(row => $"{row.Entry} {Status(row)} ok"), answers);
    }

    // The catch-all is probed with a path no route serves, and a redemption with a well-formed body holding a token
    // that is not one, which the pipeline refuses before any store read.
    private static HttpRequestMessage Request(AnonymousEndpointInventoryTests.Row row, int port)
    {
        var route = row.Route == "/{**path}" ? "/v1/anything" : row.Route;
        var request = new HttpRequestMessage(row.Method == "*" ? HttpMethod.Get : new HttpMethod(row.Method), $"http://127.0.0.1:{port}{route}");
        if (row.Method == "POST")
        {
            request.Content = new StringContent("{\"token\":\"x\"}", Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static int Status(AnonymousEndpointInventoryTests.Row row) => row.Route switch
    {
        "/health/live" => 200,
        "/health/ready" => 503,
        "/{**path}" => 404,
        _ => 403,
    };

    // "ok" when the answer is the endpoint's own, otherwise what it was.
    private static string Expected(AnonymousEndpointInventoryTests.Row row, HttpStatusCode status, string body) => row.Route switch
    {
        "/health/live" => "ok",
        "/health/ready" => body.Contains("\"checks\":[", StringComparison.Ordinal) ? "ok" : body,
        "/{**path}" => Readiness.TrafficListenerTests.Code(body) == "core.resource.not_found" ? "ok" : body,
        _ => status == HttpStatusCode.Forbidden && Readiness.TrafficListenerTests.Code(body) == "operator.redemption_failed" ? "ok" : body,
    };
}

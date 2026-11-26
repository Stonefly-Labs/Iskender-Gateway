using System.Net;
using System.Text;
using Iskender.Container.Tests.Fixture;
using Iskender.ControlPlane.Host;
using Iskender.Domain.Ports;
using Iskender.Gateway.Host;
using Iskender.Hosting;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;

namespace Iskender.Container.Tests.Listeners;

/// <summary>
/// The control plane's traffic listener (ISK-0010 T037, US4.3, US4.4, FR-021, container contract Listeners rule 5,
/// operator-security contract Anonymous endpoint inventory rule 3): it serves the admin routes and no health endpoint,
/// the health listener serves no admin route, the data plane's traffic listener is unchanged, and each plane's and the
/// worker's readiness body is what it was before ISK-0010 for the same state, holding no installation state.
/// </summary>
public class ControlPlaneTrafficListenerTests
{
    private const string Leases = ",\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task AHealthPathOnTheControlPlanesTrafficListenerAnswers404WithNoBody(string path)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(host.AddStubs));
        await host.WhenStartedAsync(client);

        using var response = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}{path}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Theory]
    [InlineData("GET", "/admin/v1/installation")]
    [InlineData("POST", "/admin/v1/bootstrap")]
    [InlineData("POST", "/admin/v1/recovery")]
    public async Task TheAdminRoutesAnswerOnTheTrafficListenerOnly(string method, string route)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(host.AddStubs));
        await host.WhenStartedAsync(client);

        using var traffic = await client.SendAsync(Request(method, host.TrafficPort, route), Token);
        using var health = await client.SendAsync(Request(method, host.HealthPort, route), Token);

        Assert.NotEqual(HttpStatusCode.NotFound, traffic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, health.StatusCode);
        Assert.Empty(await health.Content.ReadAsByteArrayAsync(Token));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Theory]
    [InlineData("GET", "/v1/anything")]
    [InlineData("POST", "/admin/v1/bootstrap")]
    [InlineData("GET", "/admin/v1/installation")]
    public async Task TheDataPlanesTrafficListenerIsUnchangedAndServesNoAdminRoute(string method, string route)
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.DataPlane(host.AddStubs));
        await host.WhenStartedAsync(client);

        using var response = await client.SendAsync(Request(method, host.TrafficPort, route), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("core.resource.not_found", Readiness.TrafficListenerTests.Code(await response.Content.ReadAsStringAsync(Token)));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    // The control plane as ISK-0009 composed it, with neither operator module and no traffic listener, gives the
    // readiness body container contract version 2 records for the same state.
    [Fact]
    public async Task TheControlPlanesReadinessBodyIsTheBodyItGaveBeforeIsk0010ForTheSameState()
    {
        var now = await ReadyBodyAsync(PlaneDefinitions.ControlPlane, []);
        var before = await ReadyBodyAsync(Isk0009ControlPlane, []);

        Assert.Equal(before, now);
        AssertNoInstallationState(now);
    }

    // The data plane and the worker as ISK-0009 composed them give the same body, byte for byte, for the same state, and
    // it holds no installation state (US4.4; CONV-0010-010). "Now" is the shipped definitions, with the control plane's
    // work command for the worker, so a readiness change in either fails here (CONV-0010-018).
    [Theory]
    [InlineData("data-plane")]
    [InlineData("worker")]
    public async Task TheDataPlanesAndTheWorkersReadinessBodiesAreTheBodiesTheyGaveBeforeIsk0010ForTheSameState(string process)
    {
        var (now, before) = process == "data-plane"
            ? (await ReadyBodyAsync(also => Shipped(DataPlaneDefinition.Create(), also), []), await ReadyBodyAsync(Isk0009DataPlane, []))
            : (await ReadyBodyAsync(also => Shipped(ControlPlaneDefinition.Create(), also), ["work"], Leases), await ReadyBodyAsync(Isk0009ControlPlaneWithWorker, ["work"], Leases));

        Assert.Equal(before, now);
        Assert.Contains("\"checks\":[{\"name\":\"lifecycle\"", now, StringComparison.Ordinal);
        AssertNoInstallationState(now);
    }

    // A shipped definition with the test host's stubs composed after its own registrations, in the plane and in each serving
    // variant, as the mirror's "also" does.
    private static ContainerPlaneDefinition Shipped(ContainerPlaneDefinition plane, Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? also) =>
        plane with
        {
            Compose = (services, sources) =>
            {
                plane.Compose(services, sources);
                also?.Invoke(services);
            },
            ServingCommands = plane.ServingCommands.ToDictionary(
                command => command.Key,
                command => command.Value with
                {
                    Compose = (services, sources) =>
                    {
                        command.Value.Compose(services, sources);
                        also?.Invoke(services);
                    },
                }),
        };

    private static void AssertNoInstallationState(string body)
    {
        foreach (var word in new[] { "installation", "initialized", "operator", "bootstrap", "identity" })
        {
            Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<string> ReadyBodyAsync(Func<Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>?, ContainerPlaneDefinition> plane, string[] args, string sections = "")
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"", PlaneDefinitions.Persistence + sections);
        using var client = new HttpClient();
        var run = await host.StartAsync(plane(host.AddStubs), args);
        await host.WhenStartedAsync(client);

        using var response = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/ready", Token);
        var body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(Token));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
        return body;
    }

    private static HttpRequestMessage Request(string method, int port, string route) =>
        new(new HttpMethod(method), $"http://127.0.0.1:{port}{route}")
        {
            Content = method == "POST" ? new StringContent("{\"token\":\"x\"}", Encoding.UTF8, "application/json") : null,
        };

    // The data plane as ISK-0009 composed it: its root, the modules, and MapIskender("/").
    private static ContainerPlaneDefinition Isk0009DataPlane(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? also) =>
        new(
            CompositionPlane.DataPlane,
            "iskender-data-plane",
            (services, sources) =>
            {
                services.AddIskender(options =>
                {
                    options.BaseDocument = TestPlanes.Hosting(sources.BaseDocument);
                    options.OverlayDocument = TestPlanes.Hosting(sources.OverlayDocument);
                    options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
                    options.EnvironmentName = sources.EnvironmentName;
                });
                Isk0009Modules(services);
                also?.Invoke(services);
            },
            app => app.MapIskender("/"),
            new Dictionary<string, ContainerCommand>());

    // The control plane as ISK-0009 composed it, with its work command: the worker root, the modules, and the dispatcher.
    private static ContainerPlaneDefinition Isk0009ControlPlaneWithWorker(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? also) =>
        Isk0009ControlPlane(also) with
        {
            ServingCommands = new Dictionary<string, ContainerServingCommand>
            {
                ["work"] = new(
                    CompositionPlane.Worker,
                    "iskender-worker",
                    (services, sources) =>
                    {
                        services.AddIskenderWorker(options =>
                        {
                            options.BaseDocument = sources.BaseDocument;
                            options.OverlayDocument = sources.OverlayDocument;
                            options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
                            options.EnvironmentName = sources.EnvironmentName;
                        });
                        Isk0009Modules(services);
                        services.AddIskenderPostgreSqlWorkDispatcher();
                        also?.Invoke(services);
                    },
                    PostgreSqlServiceCollectionExtensions.CheckWorkSettings),
            },
        };

    private static void Isk0009Modules(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddIskenderContainer();
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
        services.AddIskenderPostgreSqlWork();
    }

    private static ContainerPlaneDefinition Isk0009ControlPlane(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? also) =>
        new(
            CompositionPlane.ControlPlane,
            "iskender-control-plane",
            (services, sources) =>
            {
                services.AddIskenderControlPlane(options =>
                {
                    options.BaseDocument = sources.BaseDocument;
                    options.OverlayDocument = sources.OverlayDocument;
                    options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
                    options.EnvironmentName = sources.EnvironmentName;
                });
                services.AddIskenderContainer();
                services.AddIskenderCoordination();
                services.AddIskenderPostgreSqlPersistence();
                services.AddIskenderPostgreSqlCoordination();
                services.AddIskenderRedisCoordination();
                services.AddIskenderPostgreSqlWork();
                also?.Invoke(services);
            },
            null,
            new Dictionary<string, ContainerCommand>());
}

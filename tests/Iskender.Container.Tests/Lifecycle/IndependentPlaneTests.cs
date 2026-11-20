using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// User Story 2, acceptance scenarios 1 and 5, in process: each plane starts and composes with the other absent, and a
/// primitive set to <c>postgresql</c> is served by the PostgreSQL coordinator its module registration adds, and by no
/// other (container contract, Entry points rules 2 and 3).
/// </summary>
public class IndependentPlaneTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheControlPlaneStartsAndComposesWithNoDataPlane()
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane());

        var (_, body) = await host.WhenStartedAsync(client);

        Assert.Equal("started", ContainerTestHost.Reason(body, "lifecycle"));
        Assert.Equal("container,control-plane,coordination,coordination-postgresql,coordination-redis,persistence-postgresql,work-postgresql", host.Log.Only(6001).Value("Modules"));
        Assert.False(await ContainerTestHost.IsListeningAsync(host.TrafficPort));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task TheDataPlaneStartsAndServesWithNoControlPlane()
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.DataPlane());

        var (_, body) = await host.WhenStartedAsync(client);
        using var response = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}/v1/anything", Token);

        Assert.Equal("started", ContainerTestHost.Reason(body, "lifecycle"));
        Assert.Equal("container,coordination,coordination-postgresql,coordination-redis,hosting,persistence-postgresql,work-postgresql", host.Log.Only(6001).Value("Modules"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("core.resource.not_found", Readiness.TrafficListenerTests.Code(await response.Content.ReadAsStringAsync(Token)));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task APrimitiveOnPostgreSqlIsServedByThePostgreSqlCoordinatorAndNoOther()
    {
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence + ",\"coordination\":{\"leases\":{\"coordinator\":\"postgresql\"}}");
        var capture = new ProviderCapture();
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(capture.AddTo));
        await host.WhenStartedAsync(client);

        var services = await capture.Provider;

        Assert.IsType<PostgreSqlLeaseCoordinator>(services.GetRequiredService<ILeaseCoordinator>());
        Assert.IsType<LocalAtomicOperations>(services.GetRequiredService<IAtomicOperations>());
        Assert.IsType<LocalVersionedCache>(services.GetRequiredService<IVersionedCache>());
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }
}

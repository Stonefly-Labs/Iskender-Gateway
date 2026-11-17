using System.Net;
using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Container.Tests.Fixture.Database;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// User Story 2, acceptance scenario 4, and SC-005: the stop sequence (container contract, Lifecycle rule 1; research
/// R12). Readiness turns <c>stopping</c> at once, a request in flight completes inside <c>shutdownTimeout</c>, the
/// composition stops after the drain and releases the instance lease, the container is disposed, and the process exits 0.
/// </summary>
public class StopSequenceTests(PlaneDatabase database) : IClassFixture<PlaneDatabase>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFirstReadinessRequestAfterTheSignalIsStopping()
    {
        using var host = new ContainerTestHost().WithDocument("\"shutdownDelay\":\"1s\",\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        await host.WhenStartedAsync(client);

        host.Stop();
        var (status, body) = await host.ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("stopping", ContainerTestHost.Reason(body, "lifecycle"));
        Assert.Equal("control_plane", host.Log.Only(7004).Value("Plane"));
        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal("0", host.Log.Only(7005).Value("ExitCode"));
    }

    [Fact]
    public async Task ARequestInFlightCompletesThenTheCompositionStopsAndTheContainerIsDisposed()
    {
        using var host = new ContainerTestHost().WithDocument("\"shutdownTimeout\":\"30s\"");
        var journal = new StopJournal();
        using var client = new HttpClient();
        var run = await host.StartAsync(Traffic(host, journal));
        await host.WhenStartedAsync(client);
        var request = client.GetStringAsync($"http://127.0.0.1:{host.TrafficPort}{StopJournal.SlowPath}", Token);
        await journal.RequestEntered;

        host.Stop();
        await ContainerTestHost.WhenNotListeningAsync(host.HealthPort);
        Assert.DoesNotContain(StopJournal.StartupStopped, journal.Entries);
        journal.ReleaseRequest.SetResult();

        Assert.Equal("done", await request);
        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal([StopJournal.RequestCompleted, StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
    }

    // The adapter exposes no disposed state, so the same call is made before and after the stop: unreachable before, and
    // after, with its connection disposed, the adapter's internal error.
    [Fact]
    public async Task TheRedisCoordinatorIsDisposedWithTheContainer()
    {
        var redis = "\"redis\":{\"endpoints\":[\"127.0.0.1:" + ContainerTestHost.FreePort() + "\"],\"tlsMode\":\"disable\",\"connectTimeout\":\"1s\"}";
        using var host = new ContainerTestHost().WithDocument(sections: "\"coordination\":{\"atomic\":{\"coordinator\":\"redis\"}," + redis + "}");
        host.Environment[ConfigurationRoots.EnvironmentVariable] = "development";
        var capture = new ProviderCapture();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host, services =>
        {
            services.AddIskenderCoordination();
            services.AddIskenderRedisCoordination();
            capture.AddTo(services);
        }));
        await host.WhenStartedAsync(client);
        var coordinator = (await capture.Provider).GetRequiredService<RedisCoordinator>();
        var before = await Assert.ThrowsAsync<GatewayException>(() => ReadAsync(coordinator));

        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);

        var after = await Assert.ThrowsAsync<GatewayException>(() => ReadAsync(coordinator));
        Assert.Same(ErrorCodes.CoordinationUnavailable, before.Code);
        Assert.Same(ErrorCodes.Internal, after.Code);
    }

    [Fact]
    public async Task TheInstanceLeaseIsHeldThroughTheDrainAndReleasedAfterIt()
    {
        database.RequirePostgreSql();
        await MigrateAsync();
        var journal = new StopJournal();
        using var first = Host();
        using var client = new HttpClient();
        var firstRun = await first.StartAsync(Traffic(first, journal, Coordination));
        await first.WhenStartedAsync(client);
        var request = client.GetStringAsync($"http://127.0.0.1:{first.TrafficPort}{StopJournal.SlowPath}", Token);
        await journal.RequestEntered;

        // During the drain another control plane finds the lease held.
        first.Stop();
        await ContainerTestHost.WhenNotListeningAsync(first.HealthPort);
        using (var during = Host())
        {
            var duringRun = await during.StartAsync(TestPlanes.ControlPlane(during, Coordination));
            var (status, body) = await during.WhenStartedAsync(client);
            Assert.Equal((HttpStatusCode.ServiceUnavailable, "single_instance_held_by_other"), (status, ContainerTestHost.Reason(body, "single_instance")));
            during.Stop();
            Assert.Equal(ExitCodes.Success, await duringRun);
        }

        journal.ReleaseRequest.SetResult();
        Assert.Equal("done", await request);
        Assert.Equal(ExitCodes.Success, await firstRun);

        // The lease lasts a minute, so a control plane that holds it at once proves it was released, not expired.
        using var after = Host();
        var afterRun = await after.StartAsync(TestPlanes.ControlPlane(after, Coordination));
        var (afterStatus, afterBody) = await after.WhenStartedAsync(client);
        Assert.Equal((HttpStatusCode.OK, "held"), (afterStatus, ContainerTestHost.Reason(afterBody, "single_instance")));
        after.Stop();
        Assert.Equal(ExitCodes.Success, await afterRun);
    }

    private static async Task ReadAsync(RedisCoordinator coordinator) =>
        await coordinator.Atomic.ReadAsync(CoordinationScope.Operator, new CoordinationName("fixture", "resource"), Token);

    private static ContainerPlaneDefinition Traffic(ContainerTestHost host, StopJournal journal, Action<IServiceCollection>? also = null) =>
        TestPlanes.WithTraffic(host, services =>
        {
            journal.AddTo(services);
            also?.Invoke(services);
        }) with
        { MapTraffic = journal.MapSlow };

    private ContainerTestHost Host()
    {
        var host = new ContainerTestHost();
        var persistence = JsonNode.Parse(database.Document())!["iskender"]!["persistence"]!.ToJsonString();
        host.WithDocument(
            "\"readinessCacheDuration\":\"0s\"",
            "\"persistence\":" + persistence + ",\"coordination\":{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"},\"instanceLease\":{\"duration\":\"1m\"}}");
        host.Environment[ConfigurationRoots.EnvironmentVariable] = "development";
        return host;
    }

    private void Coordination(IServiceCollection services)
    {
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddSingleton<IDatabaseCredentialProvider>(database.Server.Credentials());
    }

    private async Task MigrateAsync()
    {
        var persistence = JsonNode.Parse(database.Document())!.ToJsonString();
        var services = new ServiceCollection();
        TestPlanes.Root(services, new CompositionSources(CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, persistence), null, false, "development"));
        Coordination(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await provider.GetRequiredService<IMigrationRunner>().ApplyAsync(new MigrationApplyRequest(), Token);
    }
}

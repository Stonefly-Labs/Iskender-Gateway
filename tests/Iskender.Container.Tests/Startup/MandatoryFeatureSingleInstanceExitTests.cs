using System.Net;
using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Container.Tests.Fixture.Database;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Startup;

/// <summary>
/// User Story 2, acceptance scenario 8, for <c>single_instance_violated</c>, on ISK-0004's PostgreSQL fixture: while one
/// control plane holds its instance lease, a second exits 4 for a mandatory feature, and without <c>MandatoryPath</c>
/// serves, not ready with <c>single_instance_held_by_other</c> (container contract, Readiness rule 6), and refuses the
/// feature's calls.
/// </summary>
public class MandatoryFeatureSingleInstanceExitTests(PlaneDatabase database) : IClassFixture<PlaneDatabase>
{
    private const string Reason = "single_instance_violated";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondControlPlaneWithAMandatoryFeatureExits4AndTheFirstKeepsItsLease()
    {
        await using var holder = await HolderAsync();
        using var host = Host();

        var exitCode = await host.RunAsync(Plane(host, mandatory: true));

        Assert.Equal(ExitCodes.MandatoryFeatureRefused, exitCode);
        MandatoryFeatureExitTests.AssertRefusal(host, Reason);
        Assert.Equal(SingleInstanceState.Held, (await holder.Capture.Provider).GetRequiredService<ISingleInstanceStatus>().State);
    }

    [Fact]
    public async Task WithoutMandatoryPathTheSecondServesNotReadyAndRefusesTheFeaturesCalls()
    {
        await using var holder = await HolderAsync();
        using var host = Host();
        var capture = new ProviderCapture();
        using var client = new HttpClient();
        var run = await host.StartAsync(Plane(host, mandatory: false, capture));

        var (status, body) = await host.WhenStartedAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("single_instance_held_by_other", ContainerTestHost.Reason(body, "single_instance"));
        MandatoryFeatureExitTests.AssertListed(body, Reason);
        await MandatoryFeatureExitTests.AssertCallRefusedAsync(await capture.Provider, CoordinationPrimitive.Leases);
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    private ContainerTestHost Host()
    {
        var host = new ContainerTestHost();
        var persistence = JsonNode.Parse(database.Document())!["iskender"]!["persistence"]!.ToJsonString();
        host.WithDocument(sections: "\"persistence\":" + persistence + ",\"coordination\":{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}}");
        host.Environment[ConfigurationRoots.EnvironmentVariable] = "development";
        return host;
    }

    private ContainerPlaneDefinition Plane(ContainerTestHost host, bool? mandatory, ProviderCapture? capture = null) =>
        TestPlanes.ControlPlane(host, services =>
        {
            Coordination(services);
            if (mandatory is { } path)
            {
                services.AddSingleton(MandatoryFeatureExitTests.Guarded(CoordinationPrimitive.Leases, ConsistencyLevel.Leased, path));
            }

            capture?.AddTo(services);
        });

    private void Coordination(IServiceCollection services)
    {
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddSingleton<IDatabaseCredentialProvider>(database.Server.Credentials());
    }

    // Applies the coordination tables through the composition's own runner, then starts a control plane that holds the lease.
    private async Task<Holder> HolderAsync()
    {
        database.RequirePostgreSql();
        var host = Host();
        var services = new ServiceCollection();
        string document;
        using (var reader = new StreamReader(host.Files.OpenRead($"{ContainerTestHost.ConfigDirectory}/iskender.json")))
        {
            document = await reader.ReadToEndAsync(Token);
        }

        TestPlanes.Root(services, new CompositionSources(CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, document), null, false, "development"));
        services.AddIskenderContainer();
        Coordination(services);
        await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            await provider.GetRequiredService<IMigrationRunner>().ApplyAsync(new MigrationApplyRequest(), Token);
        }

        var capture = new ProviderCapture();
        var run = await host.StartAsync(Plane(host, mandatory: null, capture));
        using var client = new HttpClient();
        await host.WhenStartedAsync(client);
        Assert.Equal(SingleInstanceState.Held, (await capture.Provider).GetRequiredService<ISingleInstanceStatus>().State);
        return new Holder(host, run, capture);
    }

    private sealed record Holder(ContainerTestHost Host, Task<int> Run, ProviderCapture Capture) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Host.Stop();
            await Run;
            Host.Dispose();
        }
    }
}

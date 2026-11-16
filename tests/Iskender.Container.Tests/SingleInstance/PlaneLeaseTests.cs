using System.Net;
using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Container.Tests.Fixture.Database;
using Iskender.Container.Tests.Startup;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Iskender.Container.Tests.SingleInstance;

/// <summary>
/// User Story 2, acceptance scenarios 2, 3, 9, and 10, on ISK-0004's PostgreSQL fixture with both planes composed as their
/// entry points compose them: each plane holds its own instance lease, a second control plane is refused or waits, and a
/// lost lease is retaken (container contract, Readiness rule 6). States are polled every 100 milliseconds, as ISK-0005's
/// single-instance tests poll, within twice the instance lease, the longest a renewal and a retake take.
/// </summary>
public class PlaneLeaseTests(PlaneDatabase database) : IClassFixture<PlaneDatabase>
{
    private static readonly TimeSpan InstanceLease = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BothPlanesHoldTheirOwnLeaseAndNeitherRefusesTheOther()
    {
        await MigrateAsync();
        using var control = Host();
        using var data = Host();
        using var client = new HttpClient();
        var controlRun = await control.StartAsync(PlaneDefinitions.ControlPlane(Credentials));
        var dataRun = await data.StartAsync(PlaneDefinitions.DataPlane(Credentials));

        var (controlStatus, controlBody) = await control.WhenStartedAsync(client);
        var (dataStatus, dataBody) = await data.WhenStartedAsync(client);

        Assert.Equal((HttpStatusCode.OK, "held"), (controlStatus, ContainerTestHost.Reason(controlBody, "single_instance")));
        Assert.Equal((HttpStatusCode.OK, "held"), (dataStatus, ContainerTestHost.Reason(dataBody, "single_instance")));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM isk_coordination.leases"));
        control.Stop();
        data.Stop();
        var exitCodes = await Task.WhenAll(controlRun, dataRun);
        Assert.Equal([ExitCodes.Success, ExitCodes.Success], exitCodes);
    }

    [Fact]
    public async Task ASecondControlPlaneWithAMandatoryFeatureExits4AndTheFirstKeepsItsLease()
    {
        await MigrateAsync();
        using var first = Host();
        using var client = new HttpClient();
        var firstRun = await first.StartAsync(PlaneDefinitions.ControlPlane(Credentials));
        await first.WhenStartedAsync(client);
        using var second = Host();

        var exitCode = await second.RunAsync(PlaneDefinitions.ControlPlane(services =>
        {
            Credentials(services);
            services.AddSingleton(MandatoryFeatureExitTests.Guarded(CoordinationPrimitive.Leases, ConsistencyLevel.Leased, mandatory: true));
        }));

        Assert.Equal(ExitCodes.MandatoryFeatureRefused, exitCode);
        Assert.Equal("single_instance_violated", second.Log.Only(7002).Value("Reason"));
        var (status, body) = await first.ReadyAsync(client);
        Assert.Equal((HttpStatusCode.OK, "held"), (status, ContainerTestHost.Reason(body, "single_instance")));
        first.Stop();
        Assert.Equal(ExitCodes.Success, await firstRun);
    }

    [Fact]
    public async Task ALostLeaseIsNotReadyUntilTheGuardRetakesIt()
    {
        await MigrateAsync();
        using var host = Host();
        using var client = new HttpClient();
        var run = await host.StartAsync(PlaneDefinitions.ControlPlane(Credentials));
        await host.WhenStartedAsync(client);

        // The lease row is removed under the guard, so its next renewal finds the lease gone.
        await ExecuteAsync("DELETE FROM isk_coordination.leases");

        var lost = await WaitForAsync(host, client, "single_instance_lost");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost);
        var retaken = await WaitForAsync(host, client, "held");
        Assert.Equal(HttpStatusCode.OK, retaken);
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task AControlPlaneThatFindsTheLeaseHeldWaitsNotReadyUntilItTakesIt()
    {
        await MigrateAsync();
        using var first = Host();
        using var client = new HttpClient();
        var firstRun = await first.StartAsync(PlaneDefinitions.ControlPlane(Credentials));
        await first.WhenStartedAsync(client);
        using var second = Host();
        var secondRun = await second.StartAsync(PlaneDefinitions.ControlPlane(Credentials));

        var (waiting, body) = await second.WhenStartedAsync(client);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "single_instance_held_by_other"), (waiting, ContainerTestHost.Reason(body, "single_instance")));

        first.Stop();
        Assert.Equal(ExitCodes.Success, await firstRun);
        Assert.Equal(HttpStatusCode.OK, await WaitForAsync(second, client, "held"));
        second.Stop();
        Assert.Equal(ExitCodes.Success, await secondRun);
    }

    private static async Task<HttpStatusCode> WaitForAsync(ContainerTestHost host, HttpClient client, string reason)
    {
        var deadline = DateTime.UtcNow + (2 * InstanceLease);
        while (true)
        {
            var (status, body) = await host.ReadyAsync(client);
            if (ContainerTestHost.Reason(body, "single_instance") == reason)
            {
                return status;
            }

            Assert.True(DateTime.UtcNow < deadline, $"single_instance was not {reason} within {2 * InstanceLease}.");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }
    }

    private ContainerTestHost Host()
    {
        database.RequirePostgreSql();
        var host = new ContainerTestHost();
        var persistence = JsonNode.Parse(database.Document())!["iskender"]!["persistence"]!.ToJsonString();
        host.WithDocument(
            "\"readinessCacheDuration\":\"0s\"",
            "\"persistence\":" + persistence
                + ",\"coordination\":{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"},\"instanceLease\":{\"duration\":\"5s\"}}");
        host.Environment[ConfigurationRoots.EnvironmentVariable] = "development";
        return host;
    }

    private void Credentials(IServiceCollection services) =>
        services.AddSingleton<IDatabaseCredentialProvider>(database.Server.Credentials());

    // Applies every module the planes compose, through the composition's own runner, so readiness finds the schema current.
    private async Task MigrateAsync()
    {
        database.RequirePostgreSql();
        var document = JsonNode.Parse(database.Document())!;
        document["iskender"]!["container"] = JsonNode.Parse("{}");
        var services = new ServiceCollection();
        PlaneDefinitions.ControlPlane(Credentials).Compose(
            services,
            new CompositionSources(CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, document.ToJsonString()), null, false, "development"));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await provider.GetRequiredService<IMigrationRunner>().ApplyAsync(new MigrationApplyRequest(), Token);
        await ExecuteAsync("DELETE FROM isk_coordination.leases");
    }

    // As the runtime login, in operator scope, which the tables' forced row-level policy requires of every session.
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SET iskender.scope_kind = 'operator'; " + sql, connection);
        return await command.ExecuteScalarAsync(Token);
    }

    private async Task ExecuteAsync(string sql) => await ScalarAsync(sql);
}

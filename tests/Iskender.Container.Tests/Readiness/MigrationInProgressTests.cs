using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US4.7: a migration in the compatibility window leaves the process ready.</summary>
public class MigrationInProgressTests
{
    [Fact]
    public async Task AMigrationInProgressIsReady()
    {
        using var host = new ContainerTestHost().WithDocument();
        host.Persistence.Result = PersistenceHealthResult.Unhealthy(PersistenceHealthReason.MigrationInProgress);
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        var (status, body) = await host.WhenStartedAsync(client);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("migration_in_progress", ContainerTestHost.Reason(body, "persistence"));
        Assert.Equal("ready", ContainerTestHost.Status(body, "persistence"));
        host.Stop();
        await run;
    }
}

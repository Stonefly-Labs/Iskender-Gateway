using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.1 and SC-003: each persistence reason, and whether it is ready (container contract, Readiness rule 3).</summary>
public class PersistenceReasonTests
{
    [Theory]
    [InlineData(PersistenceHealthReason.Ok, "ok", true)]
    [InlineData(PersistenceHealthReason.MigrationInProgress, "migration_in_progress", true)]
    [InlineData(PersistenceHealthReason.Unreachable, "unreachable", false)]
    [InlineData(PersistenceHealthReason.AuthenticationFailed, "authentication_failed", false)]
    [InlineData(PersistenceHealthReason.Timeout, "timeout", false)]
    [InlineData(PersistenceHealthReason.SchemaBehind, "schema_behind", false)]
    [InlineData(PersistenceHealthReason.InsufficientPrivilege, "insufficient_privilege", false)]
    [InlineData(PersistenceHealthReason.Unexpected, "unexpected", false)]
    [InlineData(PersistenceHealthReason.ChecksumMismatch, "checksum_mismatch", false)]
    [InlineData(PersistenceHealthReason.SchemaAhead, "schema_ahead", false)]
    public async Task EachReasonIsReportedWithItsReadiness(PersistenceHealthReason reason, string code, bool ready)
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        host.Persistence.Result = new PersistenceHealthResult(reason == PersistenceHealthReason.Ok ? PersistenceHealthStatus.Healthy : PersistenceHealthStatus.Unhealthy, reason);
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        var (status, body) = await host.WhenStartedAsync(client);

        Assert.Equal(code, ContainerTestHost.Reason(body, "persistence"));
        Assert.Equal(ready ? "ready" : "not_ready", ContainerTestHost.Status(body, "persistence"));
        Assert.Equal(ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(ready ? "ready" : "not_ready", body.GetProperty("status").GetString());
        host.Stop();
        await run;
    }
}

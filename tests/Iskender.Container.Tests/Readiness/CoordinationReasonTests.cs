using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.2: each coordination reason, with both forms of <c>not_configured</c> (container contract, Readiness rule 4).</summary>
public class CoordinationReasonTests
{
    public static TheoryData<CoordinationHealthStatus, CoordinationHealthReason, string, bool> Reasons => new()
    {
        { CoordinationHealthStatus.Healthy, CoordinationHealthReason.Ok, "ok", true },
        { CoordinationHealthStatus.Healthy, CoordinationHealthReason.NotConfigured, "not_configured", true },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.NotConfigured, "not_configured", false },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.Unreachable, "unreachable", false },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.AuthenticationFailed, "authentication_failed", false },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.Timeout, "timeout", false },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.ReadOnly, "read_only", false },
        { CoordinationHealthStatus.Unhealthy, CoordinationHealthReason.Unexpected, "unexpected", false },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task EachReasonIsReportedWithItsReadiness(CoordinationHealthStatus health, CoordinationHealthReason reason, string code, bool ready)
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        host.Coordination.Result = new CoordinationHealthResult(health, reason);
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        var (_, body) = await host.WhenStartedAsync(client);

        Assert.Equal(code, ContainerTestHost.Reason(body, "coordination"));
        Assert.Equal(ready ? "ready" : "not_ready", ContainerTestHost.Status(body, "coordination"));
        Assert.Equal(ready ? "ready" : "not_ready", body.GetProperty("status").GetString());
        host.Stop();
        await run;
    }
}

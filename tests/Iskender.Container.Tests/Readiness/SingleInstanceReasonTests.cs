using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>Each single-instance state (container contract, Readiness rule 5).</summary>
public class SingleInstanceReasonTests
{
    [Theory]
    [InlineData(SingleInstanceState.Held, "held", true)]
    [InlineData(SingleInstanceState.NotApplicable, "not_applicable", true)]
    [InlineData(SingleInstanceState.Unverified, "unverified", true)]
    [InlineData(SingleInstanceState.Lost, "single_instance_lost", false)]
    [InlineData(SingleInstanceState.HeldByOther, "single_instance_held_by_other", false)]
    public async Task EachStateIsReportedWithItsReadiness(SingleInstanceState state, string code, bool ready)
    {
        using var host = new ContainerTestHost().WithDocument();
        host.SingleInstance.Value = state;
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        var (_, body) = await host.WhenStartedAsync(client);

        Assert.Equal(code, ContainerTestHost.Reason(body, "single_instance"));
        Assert.Equal(ready ? "ready" : "not_ready", ContainerTestHost.Status(body, "single_instance"));
        host.Stop();
        await run;
    }
}

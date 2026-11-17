using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>
/// US3.3 and SC-003: liveness calls no probe, however often it is asked, on either plane, and whether the process is
/// ready, not ready, starting, or stopping.
/// </summary>
public class LivenessProbeCountTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AThousandLivenessRequestsMakeNoProbeCall()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        await LiveAsync(host, client, 1000);

        AssertNoProbeCall(host);
        host.Stop();
        await run;
    }

    [Fact]
    public async Task OnTheDataPlaneLivenessMakesNoProbeCall()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.DataPlane(host));

        await LiveAsync(host, client, 100);

        AssertNoProbeCall(host);
        host.Stop();
        await run;
    }

    [Fact]
    public async Task WhileNotReadyLivenessMakesNoProbeCall()
    {
        using var host = new ContainerTestHost().WithDocument();
        host.Persistence.Result = PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Unreachable);
        host.Coordination.Result = CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable);
        host.SingleInstance.Value = SingleInstanceState.Lost;
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));

        await LiveAsync(host, client, 100);

        AssertNoProbeCall(host);
        host.Stop();
        await run;
    }

    [Fact]
    public async Task WhileStartingLivenessMakesNoProbeCall()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var journal = new StopJournal();
        var run = Task.Run(() => host.RunAsync(TestPlanes.ControlPlane(host, services => journal.AddTo(services, StopJournal.StartupBehavior.UntilReleased))), Token);
        await journal.StartupEntered.WaitAsync(Token);
        await ContainerTestHost.WhenListeningAsync(host.HealthPort, run);

        await LiveAsync(host, client, 100);

        AssertNoProbeCall(host);
        Assert.False(run.IsCompleted);
        journal.ReleaseStartup.SetResult();
        host.Stop();
        await run;
    }

    [Fact]
    public async Task WhileStoppingLivenessMakesNoProbeCall()
    {
        // On the manual clock shutdownDelay does not pass until the test advances it, so the server serves while stopping.
        using var host = new ContainerTestHost().WithDocument("\"shutdownDelay\":\"10s\"");
        host.Time = host.Clock;
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        host.Stop();

        await LiveAsync(host, client, 100);

        AssertNoProbeCall(host);
        Assert.Single(host.Log.ById(7004));
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await run;
    }

    private static async Task LiveAsync(ContainerTestHost host, HttpClient client, int requests)
    {
        for (var request = 0; request < requests; request++)
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/live", Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static void AssertNoProbeCall(ContainerTestHost host)
    {
        Assert.Equal(0, host.Persistence.Calls);
        Assert.Equal(0, host.Coordination.Calls);
        Assert.Equal(0, host.SingleInstance.Reads);
    }
}

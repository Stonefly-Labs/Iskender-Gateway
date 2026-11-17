using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// FR-017, TR-8, SC-005, and data-model "Lifecycle" ("start: listeners bound, lifecycle <c>starting</c>"): the health
/// listener answers over HTTP while a composition start-up still holds the host's start, with liveness <c>200</c> and
/// readiness <c>503</c> <c>starting</c>, and <c>503</c> <c>stopping</c> once a stop signal arrives.
/// </summary>
public class ListenersDuringStartupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhileAStartUpBlocksLivenessAnswers200AndReadiness503Starting()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var journal = new StopJournal();
        var run = await StartBlockedAsync(host, journal);

        using (var live = await client.GetAsync($"http://127.0.0.1:{host.HealthPort}/health/live", Token))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        var (status, body) = await host.ReadyAsync(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(("not_ready", "starting"), (ContainerTestHost.Status(body, "lifecycle"), ContainerTestHost.Reason(body, "lifecycle")));
        Assert.False(run.IsCompleted);

        journal.ReleaseStartup.SetResult();
        var (_, started) = await host.WhenStartedAsync(client);
        Assert.Equal("started", ContainerTestHost.Reason(started, "lifecycle"));
        host.Stop();
        Assert.Equal(ExitCodes.Success, await run);
    }

    [Fact]
    public async Task AStopSignalWhileAStartUpBlocksTurnsReadiness503Stopping()
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var journal = new StopJournal();
        var run = await StartBlockedAsync(host, journal);

        host.Stop();
        var (status, body) = await host.ReadyAsync(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(("not_ready", "stopping"), (ContainerTestHost.Status(body, "lifecycle"), ContainerTestHost.Reason(body, "lifecycle")));
        Assert.False(run.IsCompleted);

        journal.ReleaseStartup.SetResult();
        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal([StopJournal.StartupFinished, StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
    }

    // A start-up that ignores cancellation holds the host's start until the test releases it; the health listener binds meanwhile.
    private static async Task<Task<int>> StartBlockedAsync(ContainerTestHost host, StopJournal journal)
    {
        var run = Task.Run(() => host.RunAsync(TestPlanes.ControlPlane(host, services => journal.AddTo(services, StopJournal.StartupBehavior.UntilReleased))), Token);
        await journal.StartupEntered.WaitAsync(Token);
        await ContainerTestHost.WhenListeningAsync(host.HealthPort, run);
        Assert.False(run.IsCompleted);
        return run;
    }
}

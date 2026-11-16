using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Health;
using Iskender.Infrastructure.Container.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// User Story 2, acceptance scenario 6, and SC-005: a stop signal during start-up turns readiness <c>stopping</c> at once,
/// waits for the composition start-up to finish or be cancelled before anything is disposed, and exits 0 (container
/// contract, Lifecycle rule 2). The shipped coordinators connect to Redis only on a first call, never in a start-up, so a
/// composition start-up that blocks stands in for that first connection.
/// </summary>
public class StopDuringStartupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStartUpThatIsCancelledEndsBeforeTheContainerIsDisposed()
    {
        using var host = new ContainerTestHost().WithDocument();
        var journal = new StopJournal();
        var run = Task.Run(() => host.RunAsync(Plane(host, journal, StopJournal.StartupBehavior.UntilCancelled)), Token);
        await journal.StartupEntered.WaitAsync(Token);
        var lifecycle = (await journal.StartupServices).GetRequiredService<ContainerLifecycle>();

        // The cancelled start-up lets the sequence run on at once, so only what the signal changes synchronously is read.
        host.Stop();
        Assert.Equal(ContainerLifecycle.Stopping, lifecycle.Phase);
        Assert.Single(host.Log.ById(7004));

        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal([StopJournal.StartupCancelled, StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
        AssertNothingRunning(host);
    }

    [Fact]
    public async Task AStartUpThatIgnoresCancellationFinishesBeforeTheContainerIsDisposed()
    {
        using var host = new ContainerTestHost().WithDocument();
        var journal = new StopJournal();
        var run = Task.Run(() => host.RunAsync(Plane(host, journal, StopJournal.StartupBehavior.UntilReleased)), Token);
        await journal.StartupEntered.WaitAsync(Token);

        host.Stop();
        await AssertStoppingAsync(host, await journal.StartupServices);
        Assert.False(run.IsCompleted);
        Assert.DoesNotContain(StopJournal.Disposed, journal.Entries);
        journal.ReleaseStartup.SetResult();

        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal([StopJournal.StartupFinished, StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
        AssertNothingRunning(host);
    }

    private static ContainerPlaneDefinition Plane(ContainerTestHost host, StopJournal journal, StopJournal.StartupBehavior behavior) =>
        TestPlanes.ControlPlane(host, services => journal.AddTo(services, behavior));

    // The phase and 7004 change at once, from inside the container, while the start-up still holds the host's start.
    private static async Task AssertStoppingAsync(ContainerTestHost host, IServiceProvider services)
    {
        Assert.Equal(ContainerLifecycle.Stopping, services.GetRequiredService<ContainerLifecycle>().Phase);
        Assert.Single(host.Log.ById(7004));
        var report = await services.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(Token);
        Assert.Equal(("lifecycle", false, "stopping"), (report.Checks[0].Name, report.Checks[0].Ready, report.Checks[0].Reason));
    }

    private static void AssertNothingRunning(ContainerTestHost host)
    {
        Assert.Equal("0", host.Log.Only(7005).Value("ExitCode"));
        Assert.False(ContainerTestHost.IsListeningAsync(host.HealthPort).GetAwaiter().GetResult());
    }
}

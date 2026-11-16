using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// Container contract, Lifecycle rule 1.3: a request still in flight after <c>shutdownTimeout</c> is cancelled, the
/// composition still stops, and the process exits 0.
/// </summary>
public class ShutdownTimeoutTests
{
    [Fact]
    public async Task ARequestStillInFlightAfterTheTimeoutIsCancelledAndTheCompositionStillStops()
    {
        using var host = new ContainerTestHost().WithDocument("\"shutdownTimeout\":\"1s\"");
        var journal = new StopJournal();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.WithTraffic(host, services => journal.AddTo(services)) with { MapTraffic = journal.MapSlow });
        await host.WhenStartedAsync(client);
        var request = client.GetAsync($"http://127.0.0.1:{host.TrafficPort}{StopJournal.SlowPath}", TestContext.Current.CancellationToken);
        await journal.RequestEntered;

        host.Stop();

        Assert.Equal(ExitCodes.Success, await run);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => request);
        Assert.Equal([StopJournal.RequestCancelled, StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
        Assert.Equal("0", host.Log.Only(7005).Value("ExitCode"));
    }
}

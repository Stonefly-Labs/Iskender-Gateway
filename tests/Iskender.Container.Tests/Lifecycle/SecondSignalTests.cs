using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Container.Tests.Lifecycle;

/// <summary>
/// Container contract, Lifecycle rule 3: a second stop signal, which the console lifetime turns into another
/// <c>StopApplication</c>, neither writes 7004 again nor shortens <c>shutdownDelay</c>, and the composition still stops.
/// </summary>
public class SecondSignalTests
{
    [Fact]
    public async Task ASecondSignalDoesNotShortenTheSequence()
    {
        using var host = new ContainerTestHost().WithDocument("\"shutdownDelay\":\"10s\",\"readinessCacheDuration\":\"0s\"");
        host.Time = host.Clock;
        var journal = new StopJournal();
        var capture = new ProviderCapture();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host, services =>
        {
            journal.AddTo(services);
            capture.AddTo(services);
        }));
        await host.WhenStartedAsync(client);

        host.Stop();
        (await capture.Provider).GetRequiredService<IHostApplicationLifetime>().StopApplication();

        // On the manual clock the delay has not passed, so the server still serves: the sequence was not shortened.
        var (status, body) = await host.ReadyAsync(client);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "stopping"), (status, ContainerTestHost.Reason(body, "lifecycle")));
        Assert.Single(host.Log.ById(7004));
        Assert.DoesNotContain(StopJournal.StartupStopped, journal.Entries);

        host.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal([StopJournal.StartupStopped, StopJournal.Disposed], journal.Entries);
    }
}

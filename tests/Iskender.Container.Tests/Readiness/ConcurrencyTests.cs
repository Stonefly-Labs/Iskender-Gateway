using System.Diagnostics;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container.Health;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.4: concurrent readiness requests share one call per probe, and each answers within its bound.</summary>
public class ConcurrencyTests
{
    // On the manual clock, the 2-second result stays fresh until the test moves the clock, so a request that arrives
    // after the shared call has finished reuses it instead of starting a call of its own.
    [Fact]
    public async Task AHundredConcurrentRequestsMakeOneCallPerProbe()
    {
        using var host = new ContainerTestHost().WithDocument();
        host.Time = host.Clock;
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        await host.WhenStartedAsync(client);
        host.Clock.Advance(TimeSpan.FromSeconds(3));
        var persistenceBefore = host.Persistence.Calls;
        var coordinationBefore = host.Coordination.Calls;
        host.Persistence.Hold();
        host.Coordination.Hold();

        var requests = Enumerable.Range(0, 100).Select(_ => host.ReadyAsync(client)).ToList();
        await host.Persistence.WhenCalledAsync(persistenceBefore + 1);
        await host.Coordination.WhenCalledAsync(coordinationBefore + 1);
        host.Persistence.Release();
        host.Coordination.Release();
        await Task.WhenAll(requests);

        Assert.Equal(persistenceBefore + 1, host.Persistence.Calls);
        Assert.Equal(coordinationBefore + 1, host.Coordination.Calls);
        host.Stop();
        await run;
    }

    [Fact]
    public async Task ABlockedProbeIsAnsweredWithinItsTimeoutPlusTheAllowance()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        await host.WhenStartedAsync(client);
        host.Persistence.Hold();
        host.Coordination.Hold();

        var watch = Stopwatch.StartNew();
        var answers = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => host.ReadyAsync(client)));
        watch.Stop();

        // No persistence module is composed, so the stub has the default 5-second timeout; both probes run concurrently.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5) + ReadinessEvaluator.ProbeAllowance + TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
        Assert.All(answers, answer => Assert.Equal("unexpected", ContainerTestHost.Reason(answer.Body, "persistence")));
        Assert.All(answers, answer => Assert.Equal("unexpected", ContainerTestHost.Reason(answer.Body, "coordination")));
        host.Persistence.Release();
        host.Coordination.Release();
        host.Stop();
        await run;
    }
}

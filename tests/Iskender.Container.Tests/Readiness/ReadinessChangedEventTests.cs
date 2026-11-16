using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Readiness;

/// <summary>US3.6: one event 7003 per change in a check's reason, and none when nothing changes.</summary>
public class ReadinessChangedEventTests
{
    [Fact]
    public async Task AChangedReasonWritesOneEventNamingTheCheckAndBothReasons()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        await host.WhenStartedAsync(client);
        await host.ReadyAsync(client);
        int Changes() => host.Log.ById(7003).Count(entry => entry.Value("Check") == "persistence");
        Assert.Equal(1, Changes());

        host.Persistence.Result = PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Unreachable);
        await host.ReadyAsync(client);
        await host.ReadyAsync(client);

        var persistence = host.Log.ById(7003).Where(entry => entry.Value("Check") == "persistence").ToList();
        Assert.Equal(2, persistence.Count);
        Assert.Equal(("-", "ok"), (persistence[0].Value("From"), persistence[0].Value("To")));
        Assert.Equal(("ok", "unreachable"), (persistence[1].Value("From"), persistence[1].Value("To")));
        host.Stop();
        await run;
    }

    [Fact]
    public async Task NoEventIsWrittenWhenNothingChanges()
    {
        using var host = new ContainerTestHost().WithDocument("\"readinessCacheDuration\":\"0s\"");
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host));
        await host.WhenStartedAsync(client);
        var before = host.Log.ById(7003).Count;

        await host.ReadyAsync(client);
        await host.ReadyAsync(client);

        Assert.Equal(before, host.Log.ById(7003).Count);
        host.Stop();
        await run;
    }
}

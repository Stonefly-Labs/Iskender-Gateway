using Iskender.Core.Contracts.Configuration;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>The <c>work-dispatcher</c> start-up (ISK-0009 T021): its place in the start order, and events 8001 to 8003.</summary>
public class StartupTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheStartupSortsAfterCoordinationSoItStartsAfterItAndStopsBeforeIt()
    {
        // Hosting runs start-ups in ordinal module-name order, and stops them in reverse (hosting contract, Start-up rules 6 and 8).
        Assert.Equal([SingleInstanceGuard.Module, WorkDispatcherStartup.Module], new[] { WorkDispatcherStartup.Module, SingleInstanceGuard.Module }.Order(StringComparer.Ordinal));
        Assert.Equal("work-dispatcher", new WorkDispatcherStartup(() => throw new InvalidOperationException(), logger: null).ModuleName);
    }

    [Fact]
    public async Task StartAndStopWriteEachLifecycleEventOnce()
    {
        work.RequirePostgreSql();
        var logger = new CapturingDiagnostics.Logger();
        var created = 0;
        await using var startup = new WorkDispatcherStartup(
            () =>
            {
                created++;
                return TestDispatcher.Create(work, [ApplyRecordingHandler.Registration()], "", injection: null, out _, out _);
            },
            logger);

        await startup.StartAsync(EmptySnapshot(), Token);
        await startup.StopAsync(Token);
        await startup.StopAsync(Token);

        Assert.Equal(1, created);
        Assert.Equal([8001, 8002, 8003], logger.Events.Select(logged => logged.Id.Id));
        Assert.Equal("drained", logger.Events[2].Fields.Single(field => field.Key == "Outcome").Value);
    }

    [Fact]
    public async Task StopWithoutStartWritesNothing()
    {
        var logger = new CapturingDiagnostics.Logger();
        await using var startup = new WorkDispatcherStartup(() => throw new InvalidOperationException(), logger);

        await startup.StopAsync(Token);

        Assert.Empty(logger.Events);
    }

    private static ConfigurationSnapshot EmptySnapshot()
    {
        var report = Iskender.Domain.Registry.ConfigurationValidator.Validate(new Iskender.Domain.Registry.ConfigurationLoadRequest(
            Iskender.Domain.Registry.ConfigurationSchemaRegistry.Create([WorkSectionSchema.Schema]),
            Iskender.Domain.Registry.ConfigurationDocumentSource.FromString("empty.json", Iskender.Domain.Registry.ConfigurationDocumentFormat.Json, "{}"),
            environmentName: "development"));
        return report.Snapshot!;
    }
}

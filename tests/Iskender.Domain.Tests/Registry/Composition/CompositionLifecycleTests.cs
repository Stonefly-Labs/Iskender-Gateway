using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Domain.Tests.Configuration;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary>
/// <see cref="CompositionLifecycle"/>'s checks, which an entry point can run before the host starts, its start-up order,
/// and <see cref="CompositionStatus"/> (hosting contract, Start-up rules 1 to 6; research R1, R9).
/// </summary>
public class CompositionLifecycleTests
{
    private static readonly CompositionModule Root = new("hosting", [TestSchemas.Alpha], []);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (CompositionLifecycle Lifecycle, CompositionStatus Status, CapturingLoggerFactory Logs) Create(
        string text,
        IEnumerable<CompositionModule>? modules = null,
        IEnumerable<ICompositionStartup>? startups = null)
    {
        var logs = new CapturingLoggerFactory();
        var status = new CompositionStatus();
        var configuration = new CompositionConfiguration(
            new(CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, text), null, false, null),
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha]),
            logs);
        return (new CompositionLifecycle(modules ?? [Root], configuration, status, () => startups ?? [], logs), status, logs);
    }

    [Fact]
    public async Task EnsureComposableWrites6001OnceAndStartingFindsTheStoredOutcome()
    {
        var (lifecycle, _, logs) = Create("{}");

        await lifecycle.EnsureComposableAsync(Token);
        await lifecycle.EnsureComposableAsync(Token);
        await lifecycle.StartingAsync(Token);

        Assert.Equal(1, logs.Logger.Events.Count(entry => entry.Id.Id == 6001));
        Assert.Equal(1, logs.Logger.Events.Count(entry => entry.Id.Id == 3001));
    }

    [Fact]
    public async Task ARejectedConfigurationWrites6002OnceAndFailsEveryCall()
    {
        var (lifecycle, _, logs) = Create("{\"iskender\":{\"alpha\":{\"count\":\"x\"}}}");

        var first = await Assert.ThrowsAsync<GatewayException>(() => lifecycle.EnsureComposableAsync(Token));
        var second = await Assert.ThrowsAsync<GatewayException>(() => lifecycle.StartingAsync(Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, first.Code);
        Assert.Same(first, second);
        Assert.Equal(1, logs.Logger.Events.Count(entry => entry.Id.Id == 6002));
    }

    [Fact]
    public async Task AnUnmetRequirementWrites6003BeforeValidation()
    {
        var (lifecycle, _, logs) = Create("{}", [Root, new CompositionModule("coordination-redis", [], ["coordination"])]);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => lifecycle.EnsureComposableAsync(Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Equal([6003], logs.Logger.Events.Select(entry => entry.Id.Id));
        Assert.Same(failure, await Assert.ThrowsAsync<GatewayException>(() => lifecycle.StartingAsync(Token)));
    }

    [Fact]
    public async Task StartupsRunInModuleNameOrderThenTheStatusIsStarted()
    {
        var journal = new List<string>();
        var (lifecycle, status, _) = Create("{}", startups: [new RecordingStartup("zeta", journal), new RecordingStartup("alpha", journal)]);

        Assert.Equal(CompositionPhase.Starting, status.Phase);
        await lifecycle.StartingAsync(Token);
        await lifecycle.StartAsync(Token);

        Assert.Equal(["start alpha", "start zeta"], journal);
        Assert.Equal(CompositionPhase.Started, status.Phase);
    }

    [Fact]
    public async Task StopRunsInReverseOrderThenTheStatusIsStopped()
    {
        var journal = new List<string>();
        var (lifecycle, status, _) = Create("{}", startups: [new RecordingStartup("zeta", journal), new RecordingStartup("alpha", journal)]);
        await lifecycle.StartingAsync(Token);
        await lifecycle.StartAsync(Token);

        await lifecycle.StopAsync(Token);
        await lifecycle.StoppedAsync(Token);

        Assert.Equal(["start alpha", "start zeta", "stop zeta", "stop alpha"], journal);
        Assert.Equal(CompositionPhase.Stopped, status.Phase);
    }
}

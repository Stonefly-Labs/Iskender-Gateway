using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Health;
using Iskender.Infrastructure.Container.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Container.Tests.Readiness;

/// <summary>The evaluator on a container with no host: the check order, <c>not_composed</c>, and event 7003 (Readiness rules 1, 7, and 9).</summary>
public class ReadinessEvaluatorTests
{
    private static ServiceProvider Provider(CapturedLog log, Action<IServiceCollection>? also = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new LoggerFactory([log]));
        TestPlanes.Root(services, new CompositionSources(CompositionDocument.FromText("iskender.json", Domain.Registry.ConfigurationDocumentFormat.Json, "{}"), null, false, null));
        services.AddIskenderContainer();
        also?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task UncomposedModulesAreNotComposedAndReadyInTheContractsOrder()
    {
        using var log = new CapturedLog();
        await using var provider = Provider(log);

        var report = await provider.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["lifecycle", "persistence", "coordination", "single_instance", "capability"], report.Checks.Select(check => check.Name));
        Assert.Equal(["starting", "not_composed", "not_composed", "not_composed", "none_refused"], report.Checks.Select(check => check.Reason));
        Assert.Equal([false, true, true, true, true], report.Checks.Select(check => check.Ready));
        Assert.False(report.Ready);
        Assert.Equal(1, report.Version);
    }

    [Fact]
    public async Task TheFirstEvaluationWritesOneEventPerCheckWithADashAndTheNextNone()
    {
        using var log = new CapturedLog();
        await using var provider = Provider(log);
        var evaluator = provider.GetRequiredService<ReadinessEvaluator>();

        await evaluator.EvaluateAsync(TestContext.Current.CancellationToken);
        await evaluator.EvaluateAsync(TestContext.Current.CancellationToken);

        var changes = log.ById(7003);
        Assert.Equal(5, changes.Count);
        Assert.All(changes, change => Assert.Equal("-", change.Value("From")));
        Assert.Equal("lifecycle", changes[0].Value("Check"));
    }

    [Fact]
    public async Task AProbeThatThrowsIsUnexpectedAndNotReady()
    {
        using var log = new CapturedLog();
        await using var provider = Provider(log, services => services.AddSingleton<Domain.Ports.IPersistenceHealthProbe>(new ThrowingProbe()));

        var report = await provider.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(TestContext.Current.CancellationToken);

        var persistence = report.Checks.Single(check => check.Name == "persistence");
        Assert.Equal(("unexpected", false), (persistence.Reason, persistence.Ready));
    }

    [Fact]
    public async Task AGuardNotComposedYetIsUnverifiedAndReady()
    {
        // Before the coordination start-up composes the guard, its status fails as ComposedCoordination's accessors do.
        using var log = new CapturedLog();
        await using var provider = Provider(log, services => services.AddSingleton<Domain.Ports.ISingleInstanceStatus>(
            _ => throw new Core.Contracts.Errors.GatewayException(Core.Contracts.Errors.ErrorCodes.CoordinationCapabilityUnsupported)));

        var report = await provider.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(TestContext.Current.CancellationToken);

        var singleInstance = report.Checks.Single(check => check.Name == "single_instance");
        Assert.Equal(("unverified", true), (singleInstance.Reason, singleInstance.Ready));
    }

    [Fact]
    public async Task AnEvaluationInFlightWhenTheStopSignalArrivesReportsStopping()
    {
        using var log = new CapturedLog();
        var probe = new BlockingProbe();
        await using var provider = Provider(log, services => services.AddSingleton<Domain.Ports.IPersistenceHealthProbe>(probe));

        var evaluation = provider.GetRequiredService<ReadinessEvaluator>().EvaluateAsync(TestContext.Current.CancellationToken).AsTask();
        await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        provider.GetRequiredService<ContainerLifecycle>().BeginStopping();
        probe.Release.SetResult();

        var report = await evaluation;
        Assert.Equal(("lifecycle", false, "stopping"), (report.Checks[0].Name, report.Checks[0].Ready, report.Checks[0].Reason));
        Assert.Equal("ok", report.Checks[1].Reason);
    }

    private sealed class BlockingProbe : Domain.Ports.IPersistenceHealthProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Domain.Ports.PersistenceHealthResult> CheckAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            return Domain.Ports.PersistenceHealthResult.Healthy;
        }
    }

    private sealed class ThrowingProbe : Domain.Ports.IPersistenceHealthProbe
    {
        public ValueTask<Domain.Ports.PersistenceHealthResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}

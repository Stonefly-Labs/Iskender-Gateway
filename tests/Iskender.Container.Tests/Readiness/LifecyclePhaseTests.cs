using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container.Lifecycle;

namespace Iskender.Container.Tests.Readiness;

/// <summary>Container contract, Readiness rule 2: the lifecycle check's phases, driven by a real composition lifecycle.</summary>
public sealed class LifecyclePhaseTests : IDisposable
{
    private readonly CompositionStatus _status = new();

    private readonly CompositionConfiguration _configuration = new(new CompositionSources(null, null, false, null), ConfigurationSchemaRegistry.Create([]), null);

    private readonly CompositionLifecycle _composition;

    public LifecyclePhaseTests()
    {
        _composition = new CompositionLifecycle([], _configuration, _status, () => Array.Empty<ICompositionStartup>(), null);
    }

    public void Dispose() => _configuration.Dispose();

    [Fact]
    public async Task ThePhaseIsStartingUntilTheStartUpsFinishThenStartedThenStopping()
    {
        var lifecycle = new ContainerLifecycle(_status, () => TimeSpan.Zero, "control_plane");

        Assert.Equal("starting", lifecycle.Phase);
        Assert.False(lifecycle.IsReady);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("starting", lifecycle.Phase);

        await _composition.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("started", lifecycle.Phase);
        Assert.True(lifecycle.IsReady);

        await lifecycle.StoppingAsync(TestContext.Current.CancellationToken);
        Assert.Equal("stopping", lifecycle.Phase);
        Assert.False(lifecycle.IsReady);
    }

    [Fact]
    public async Task AStopBeforeTheStartUpsFinishIsStoppingAtOnce()
    {
        var lifecycle = new ContainerLifecycle(_status, () => TimeSpan.Zero, "control_plane");

        await lifecycle.StoppingAsync(TestContext.Current.CancellationToken);
        await _composition.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("stopping", lifecycle.Phase);
    }

    [Fact]
    public async Task StoppingStaysAfterTheStartUpsStop()
    {
        var lifecycle = new ContainerLifecycle(_status, () => TimeSpan.Zero, "control_plane");
        await _composition.StartAsync(TestContext.Current.CancellationToken);

        await lifecycle.StoppingAsync(TestContext.Current.CancellationToken);
        await _composition.StoppedAsync(TestContext.Current.CancellationToken);
        await lifecycle.StoppedAsync(TestContext.Current.CancellationToken);

        Assert.Equal("stopping", lifecycle.Phase);
    }

    [Fact]
    public void WithNoCompositionRootThePhaseStaysStarting()
    {
        var lifecycle = new ContainerLifecycle(null, () => TimeSpan.Zero, "control_plane");

        Assert.Equal("starting", lifecycle.Phase);
    }
}

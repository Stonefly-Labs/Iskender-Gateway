using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Composition;

/// <summary>
/// ISK-0009 T047 and T048, data-model "Entry points": a definition's serving commands are empty unless set, so the data
/// plane has none, the control plane has only <c>work</c>, which serves the worker, and the worker's plane name is
/// <c>worker</c>.
/// </summary>
public class ServingCommandsTests
{
    [Fact]
    public void OnlyTheControlPlaneServesWork()
    {
        using var host = new ContainerTestHost();

        Assert.Empty(PlaneDefinitions.DataPlane().ServingCommands);
        Assert.Empty(TestPlanes.DataPlane(host).ServingCommands);
        Assert.Empty(TestPlanes.ControlPlane(host).ServingCommands);
        var work = Assert.Single(PlaneDefinitions.ControlPlane().ServingCommands);
        Assert.Equal(("work", CompositionPlane.Worker, "iskender-worker"), (work.Key, work.Value.Plane, work.Value.ServiceName));
        Assert.NotNull(work.Value.Check);
    }

    [Theory]
    [InlineData(CompositionPlane.DataPlane, "data_plane")]
    [InlineData(CompositionPlane.ControlPlane, "control_plane")]
    [InlineData(CompositionPlane.Worker, "worker")]
    public void EachPlaneHasItsEventName(CompositionPlane plane, string name)
    {
        Assert.Equal(name, new ContainerPlaneDefinition(plane, "iskender-test", (_, _) => { }, null, new Dictionary<string, ContainerCommand>()).PlaneName);
    }
}

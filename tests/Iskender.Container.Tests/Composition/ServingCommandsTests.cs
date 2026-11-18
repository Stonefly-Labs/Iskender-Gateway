using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Composition;

/// <summary>
/// ISK-0009 T047, data-model "Entry points": a definition's serving commands are empty unless set, so the existing planes
/// gain none, and the worker's plane name is <c>worker</c>.
/// </summary>
public class ServingCommandsTests
{
    [Fact]
    public void TheExistingDefinitionsHaveNoServingCommand()
    {
        using var host = new ContainerTestHost();

        Assert.Empty(PlaneDefinitions.DataPlane().ServingCommands);
        Assert.Empty(PlaneDefinitions.ControlPlane().ServingCommands);
        Assert.Empty(TestPlanes.DataPlane(host).ServingCommands);
        Assert.Empty(TestPlanes.ControlPlane(host).ServingCommands);
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

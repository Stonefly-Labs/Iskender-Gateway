using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Composition;

/// <summary>
/// Container contract Entry points rule 2 (ISK-0009 T048): the data plane, the serving control plane, and the worker each
/// compose exactly the registrations the table lists, as the module names event 6001 lists in ordinal order. Only the
/// worker composes the dispatcher.
/// </summary>
public class EntryPointRegistrationTests
{
    public static TheoryData<string, string> Modules() => new()
    {
        { "data_plane", "container,coordination,coordination-postgresql,coordination-redis,hosting,persistence-postgresql,work-postgresql" },
        { "control_plane", "container,control-plane,coordination,coordination-postgresql,coordination-redis,persistence-postgresql,work-postgresql" },
        { "worker", "container,coordination,coordination-postgresql,coordination-redis,persistence-postgresql,work-dispatcher,work-postgresql,worker" },
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void EachCompositionRegistersTheModulesTheTableLists(string plane, string modules)
    {
        var services = new ServiceCollection();
        var sources = new CompositionSources(null, null, false, null);
        switch (plane)
        {
            case "data_plane":
                PlaneDefinitions.DataPlane().Compose(services, sources);
                break;
            case "control_plane":
                PlaneDefinitions.ControlPlane().Compose(services, sources);
                break;
            default:
                PlaneDefinitions.Worker().Compose(services, sources);
                break;
        }

        Assert.Equal(modules, CompositionLog.ModulesText(services.Select(descriptor => descriptor.ImplementationInstance).OfType<CompositionModule>().Select(module => module.Name)));
    }
}

using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;

// The control plane's entry point (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, with no
// traffic listener. Owned by ISK-0007.
var definition = new ContainerPlaneDefinition(
    CompositionPlane.ControlPlane,
    "iskender-control-plane",
    (services, sources) =>
    {
        services.AddIskenderControlPlane(options =>
        {
            options.BaseDocument = sources.BaseDocument;
            options.OverlayDocument = sources.OverlayDocument;
            options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
            options.EnvironmentName = sources.EnvironmentName;
        });
        services.AddIskenderContainer();
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
    },
    null,
    new Dictionary<string, ContainerCommand>());

return await ContainerEntryPoint.RunAsync(args, definition);

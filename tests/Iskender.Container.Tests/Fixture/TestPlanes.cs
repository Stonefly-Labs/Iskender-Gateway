using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Plane definitions for the in-process entry point tests. Each composes the control-plane root, which takes the resolved
/// sources as they are, the container module, and the host's stub probes.
/// </summary>
internal static class TestPlanes
{
    /// <summary>What the traffic listener answers on <c>/health/live</c>, to tell it from the health listener.</summary>
    public const string TrafficAnswer = "traffic";

    /// <summary>The control plane: no traffic listener and no command.</summary>
    public static ContainerPlaneDefinition ControlPlane(ContainerTestHost host, Action<IServiceCollection>? also = null) =>
        new(CompositionPlane.ControlPlane, "iskender-control-plane", (services, sources) => Compose(host, services, sources, also), null, new Dictionary<string, ContainerCommand>());

    /// <summary>A plane with a traffic listener whose routes answer <see cref="TrafficAnswer"/>.</summary>
    public static ContainerPlaneDefinition WithTraffic(ContainerTestHost host, Action<IServiceCollection>? also = null) =>
        new(CompositionPlane.DataPlane, "iskender-data-plane", (services, sources) => Compose(host, services, sources, also), MapTraffic, new Dictionary<string, ContainerCommand>());

    /// <summary>The control plane with the PostgreSQL persistence module, whose <c>tlsMode</c> field is development-only.</summary>
    public static ContainerPlaneDefinition WithPersistence(ContainerTestHost host) =>
        ControlPlane(host, services => services.AddIskenderPostgreSqlPersistence());

    /// <summary>A plane that composes only <paramref name="compose"/>, for registration mistakes.</summary>
    public static ContainerPlaneDefinition Only(Action<IServiceCollection, CompositionSources> compose) =>
        new(CompositionPlane.ControlPlane, "iskender-control-plane", compose, null, new Dictionary<string, ContainerCommand>());

    /// <summary>The control plane with one command.</summary>
    public static ContainerPlaneDefinition WithCommand(ContainerTestHost host, string name, ContainerCommand command) =>
        ControlPlane(host) with { Commands = new Dictionary<string, ContainerCommand> { [name] = command } };

    public static void Root(IServiceCollection services, CompositionSources sources) =>
        services.AddIskenderControlPlane(options =>
        {
            options.BaseDocument = sources.BaseDocument;
            options.OverlayDocument = sources.OverlayDocument;
            options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
            options.EnvironmentName = sources.EnvironmentName;
        });

    private static void Compose(ContainerTestHost host, IServiceCollection services, CompositionSources sources, Action<IServiceCollection>? also)
    {
        Root(services, sources);
        services.AddIskenderContainer();
        host.AddStubs(services);
        also?.Invoke(services);
    }

    private static void MapTraffic(WebApplication app)
    {
        app.MapGet("/health/live", () => TrafficAnswer);
        app.MapGet("/hello", () => TrafficAnswer);
    }
}

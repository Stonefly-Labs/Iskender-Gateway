using Iskender.Domain.Ports;
using Iskender.Hosting;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// The two planes as their entry points compose them, written out as container contract Entry points rule 2 lists the
/// registrations (TR-5): each root, then the container, coordination, PostgreSQL persistence and coordination, and Redis
/// coordination modules, with <c>MapIskender("/")</c> on the data plane's traffic listener. No stub is registered.
/// </summary>
internal static class PlaneDefinitions
{
    /// <summary>A <c>persistence</c> section, always required, whose host never resolves; start opens no connection.</summary>
    public const string Persistence =
        "\"persistence\":{\"host\":\"iskender-database.invalid\",\"database\":\"iskender\",\"username\":\"gateway\",\"password\":{\"secretRef\":\"database/runtime\"}}";

    public static ContainerPlaneDefinition ControlPlane(Action<IServiceCollection>? also = null) =>
        new(
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
                Modules(services);
                also?.Invoke(services);
            },
            null,
            new Dictionary<string, ContainerCommand>());

    public static ContainerPlaneDefinition DataPlane(Action<IServiceCollection>? also = null) =>
        new(
            CompositionPlane.DataPlane,
            "iskender-data-plane",
            (services, sources) =>
            {
                services.AddIskender(options =>
                {
                    options.BaseDocument = TestPlanes.Hosting(sources.BaseDocument);
                    options.OverlayDocument = TestPlanes.Hosting(sources.OverlayDocument);
                    options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
                    options.EnvironmentName = sources.EnvironmentName;
                });
                Modules(services);
                also?.Invoke(services);
            },
            app => app.MapIskender("/"),
            new Dictionary<string, ContainerCommand>());

    private static void Modules(IServiceCollection services)
    {
        services.AddIskenderContainer();
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
    }
}

using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;

// The data plane's entry point (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, and
// MapIskender("/") on the traffic listener. Owned by ISK-0007.
var definition = new ContainerPlaneDefinition(
    CompositionPlane.DataPlane,
    "iskender-data-plane",
    (services, sources) =>
    {
        services.AddIskender(options =>
        {
            options.BaseDocument = Document(sources.BaseDocument);
            options.OverlayDocument = Document(sources.OverlayDocument);
            options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
            options.EnvironmentName = sources.EnvironmentName;
        });
        services.AddIskenderContainer();
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
    },
    app => app.MapIskender("/"),
    new Dictionary<string, ContainerCommand>());

return await ContainerEntryPoint.RunAsync(args, definition);

// A resolved document as AddIskender(...) takes it, opened when validation reads it (maintainer decision, 2026-11-15).
static IskenderConfigurationDocument? Document(CompositionDocument? document) =>
    document is null
        ? null
        : IskenderConfigurationDocument.FromStream(
            document.SourceName,
            document.Format == ConfigurationDocumentFormat.Yaml ? IskenderDocumentFormat.Yaml : IskenderDocumentFormat.Json,
            document.OpenRead);

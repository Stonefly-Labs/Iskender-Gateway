using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;

namespace Iskender.Gateway.Host;

/// <summary>
/// The data plane's definition (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, the outbox
/// (ISK-0009), and MapIskender("/") on the traffic listener. It is a file of its own so that the anonymous endpoint
/// inventory test compiles this exact source rather than a copy of it, without referencing the host (ISK-0010
/// CONV-0010-002). Owned by ISK-0007.
/// </summary>
internal static class DataPlaneDefinition
{
    public static ContainerPlaneDefinition Create() =>
        new(
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
                services.AddIskenderPostgreSqlWork();
            },
            app => app.MapIskender("/"),
            new Dictionary<string, ContainerCommand>());

    // A resolved document as AddIskender(...) takes it, opened when validation reads it (maintainer decision, 2026-11-15).
    private static IskenderConfigurationDocument? Document(CompositionDocument? document) =>
        document is null
            ? null
            : IskenderConfigurationDocument.FromStream(
                document.SourceName,
                document.Format == ConfigurationDocumentFormat.Yaml ? IskenderDocumentFormat.Yaml : IskenderDocumentFormat.Json,
                document.OpenRead);
}

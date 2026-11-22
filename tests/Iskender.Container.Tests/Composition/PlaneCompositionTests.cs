using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Composition;

/// <summary>
/// Each plane's migration set and traffic listener (ISK-0010 T034, FR-031, FR-032, container contract Entry points rule 2
/// and Migration command rule 4): the control plane, which serves, migrates, and bootstraps through one composition, adds
/// the PostgreSQL identity and operator security modules, so its migration set includes <c>audit</c> and
/// <c>identity</c>, and the data plane and the worker have neither. <c>EntryPointRegistrationTests</c> pins each
/// composition's module list.
/// </summary>
public class PlaneCompositionTests
{
    private static readonly CompositionSources Sources =
        new(CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{" + PlaneDefinitions.Persistence + "}}"), null, false, "development");

    public static TheoryData<string, bool> Planes() => new()
    {
        { "control-plane", true },
        { "data-plane", false },
        { "worker", false },
    };

    [Theory]
    [MemberData(nameof(Planes))]
    public void OnlyTheControlPlanesMigrationSetIncludesAuditAndIdentity(string plane, bool composes)
    {
        var migrations = Compose(plane).Where(descriptor => descriptor.ServiceType == typeof(MigrationModule))
            .Select(descriptor => ((MigrationModule)descriptor.ImplementationInstance!).Name)
            .ToList();

        Assert.Equal(composes, migrations.Contains("audit"));
        Assert.Equal(composes, migrations.Contains("identity"));
    }

    [Fact]
    public void BothPlanesMapATrafficListener()
    {
        // The worker is a serving variant, which binds only its health listener.
        Assert.NotNull(PlaneDefinitions.ControlPlane().MapTraffic);
        Assert.NotNull(PlaneDefinitions.DataPlane().MapTraffic);
    }

    private static ServiceCollection Compose(string plane)
    {
        var services = new ServiceCollection();
        switch (plane)
        {
            case "control-plane":
                PlaneDefinitions.ControlPlane().Compose(services, Sources);
                break;
            case "data-plane":
                PlaneDefinitions.DataPlane().Compose(services, Sources);
                break;
            default:
                PlaneDefinitions.Worker().Compose(services, Sources);
                break;
        }

        return services;
    }
}

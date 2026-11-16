using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Container.Tests.Composition;

/// <summary>
/// User Story 6, acceptance scenarios 2 and 3, FR-006, and TR-4: one composition root per collection, module registrations
/// that need a root, and each plane validating only the sections it registers (hosting contract, Ownership rules 1 and 2).
/// </summary>
public class CompositionRootTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Modules() =>
        ["container", "coordination", "persistence-postgresql", "coordination-postgresql", "coordination-redis"];

    [Fact]
    public void AControlPlaneRootAfterADataPlaneRootFailsAtTheSecondCallNamingControlPlane()
    {
        var services = new ServiceCollection();
        services.AddIskender(_ => { });
        var before = services.Count;

        var failure = Assert.Throws<GatewayException>(() => services.AddIskenderControlPlane(_ => { }));

        AssertDuplicate(failure, ControlPlaneServiceCollectionExtensions.ControlPlaneModule);
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public void ADataPlaneRootAfterAControlPlaneRootFailsAtTheSecondCallNamingHosting()
    {
        var services = new ServiceCollection();
        services.AddIskenderControlPlane(_ => { });
        var before = services.Count;

        var failure = Assert.Throws<GatewayException>(() => services.AddIskender(_ => { }));

        AssertDuplicate(failure, "hosting");
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public void AControlPlaneRootWithANullArgumentFailsWithValueInvalid()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((IServiceCollection)null!).AddIskenderControlPlane(_ => { })).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new ServiceCollection().AddIskenderControlPlane(null!)).Code);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task AModuleWithNoRootFailsAtHostStartNamingHosting(string module)
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                Register(builder.Services, module);
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("missing", "hosting"), failure.Error!.Diagnostics!.Values);
        Assert.Equal("hosting", logs.Only(6003).Value("Missing"));
    }

    [Fact]
    public async Task AControlPlaneDocumentWithAHostingSectionIsRejected()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskenderControlPlane(options =>
                options.BaseDocument = CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"30s\"}}}")),
            ct: Token));

        AssertNamespaceUnknown(failure);
    }

    [Fact]
    public async Task ADataPlaneDocumentWithAManagementSectionIsRejected()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskender(options =>
                options.BaseDocument = IskenderConfigurationDocument.FromText("iskender.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"management\":{}}}")),
            ct: Token));

        AssertNamespaceUnknown(failure);
    }

    // Each module with the modules it requires other than hosting, so the root is the only requirement left unmet.
    private static void Register(IServiceCollection services, string module)
    {
        switch (module)
        {
            case "container":
                services.AddIskenderContainer();
                break;
            case "coordination":
                services.AddIskenderCoordination();
                break;
            case "persistence-postgresql":
                services.AddIskenderPostgreSqlPersistence();
                break;
            case "coordination-postgresql":
                services.AddIskenderCoordination();
                services.AddIskenderPostgreSqlPersistence();
                services.AddIskenderPostgreSqlCoordination();
                break;
            case "coordination-redis":
                services.AddIskenderCoordination();
                services.AddIskenderRedisCoordination();
                break;
        }
    }

    private static void AssertDuplicate(GatewayException failure, string module)
    {
        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("module", module), failure.Error!.Diagnostics!.Values);
    }

    private static void AssertNamespaceUnknown(GatewayException failure)
    {
        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains(ErrorCodes.ConfigNamespaceUnknown.Id, failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }
}

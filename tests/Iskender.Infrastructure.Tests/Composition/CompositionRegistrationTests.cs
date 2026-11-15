using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Tests.Composition;

/// <summary><see cref="CompositionRegistration"/>'s duplicate rule, its one <see cref="CompositionCheck"/>, and the check's two outcomes (research R8).</summary>
public class CompositionRegistrationTests
{
    private static readonly CompositionModule Hosting = new("hosting", [], []);

    private static readonly CompositionModule Coordination = new("coordination", [], ["hosting"]);

    private static readonly CompositionModule Persistence = new("persistence-postgresql", [], ["hosting"]);

    [Fact]
    public void AModuleIsAddedAsASingletonInstance()
    {
        var services = new ServiceCollection();

        CompositionRegistration.Add(services, Coordination);

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionModule));
        Assert.Same(Coordination, descriptor.ImplementationInstance);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ARepeatedModuleNameFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        CompositionRegistration.Add(services, Coordination);
        var before = services.ToList();

        var failure = Assert.Throws<GatewayException>(() => CompositionRegistration.Add(services, new CompositionModule("coordination", [], [])));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Equal(before, services);
    }

    [Fact]
    public void NullArgumentsFail()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionRegistration.Add(null!, Coordination)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionRegistration.Add(new ServiceCollection(), null!)).Code);
    }

    [Fact]
    public void TheCheckIsAddedOncePerCollection()
    {
        var services = new ServiceCollection();

        CompositionRegistration.Add(services, Coordination);
        CompositionRegistration.Add(services, Persistence);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionCheck));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task WithTheHostingModuleTheCheckDoesNothing()
    {
        var logger = new CapturingDiagnostics.Logger();
        var check = new CompositionCheck([Coordination, Hosting], new CompositionRoot(CompositionPlane.DataPlane, "hosting"), logger);

        await check.StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task WithoutTheHostingModuleTheCheckWrites6003AndFails()
    {
        var logger = new CapturingDiagnostics.Logger();
        var check = new CompositionCheck([Persistence, Coordination], root: null, logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        var entry = Assert.Single(logger.Events);
        Assert.Equal((6003, "IskenderRegistrationMissing", LogLevel.Error), (entry.Id.Id, entry.Id.Name, entry.Level));
        Assert.Equal("Iskender registration missing: hosting required by coordination.", entry.Message);
        Assert.Contains(failure.Error!.Diagnostics!.Values, pair => pair is { Key: "requiredBy", Value: "coordination" });
    }

    [Fact]
    public void TheCheckWritesUnderHostingsCategory()
    {
        Assert.Equal("Iskender.Hosting", CompositionCheck.Category);
    }
}

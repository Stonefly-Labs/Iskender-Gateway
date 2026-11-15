using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary><see cref="CompositionServices"/>: one root per collection, and the module seam (research R1 to R3).</summary>
public class CompositionServicesTests
{
    private static readonly SectionSchema Hosting = SectionSchema.Create("hosting", "Iskender.Hosting", 1);

    private static readonly CompositionSources NoSources = new(null, null, false, null);

    private static readonly CompositionRoot DataPlane = new(CompositionPlane.DataPlane, "hosting");

    private static readonly CompositionRoot ControlPlane = new(CompositionPlane.ControlPlane, "control-plane");

    [Fact]
    public void ARootRegistersItselfItsModuleAndTheCore()
    {
        var services = new ServiceCollection();

        CompositionServices.AddRoot(services, DataPlane, NoSources, [Hosting]);

        Assert.Same(DataPlane, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionRoot)).ImplementationInstance);
        Assert.Same(NoSources, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionSources)).ImplementationInstance);
        var module = Assert.IsType<CompositionModule>(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionModule)).ImplementationInstance);
        Assert.Equal("hosting", module.Name);
        Assert.Equal([Hosting], module.Sections);
        Assert.Empty(module.Requires);
        foreach (var type in new[]
        {
            typeof(ConfigurationSchemaRegistry), typeof(CompositionConfiguration), typeof(IValidatedConfiguration), typeof(ConfigurationSnapshot),
            typeof(ConfigurationRevision), typeof(CompositionStatus), typeof(CompositionLifecycle), typeof(IHostedService), typeof(IHostedLifecycleService),
        })
        {
            Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, descriptor => descriptor.ServiceType == type).Lifetime);
        }

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CompositionCheck));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASecondRootFailsNamingItsModuleBeforeAnythingChanges(bool sameRoot)
    {
        var services = new ServiceCollection();
        CompositionServices.AddRoot(services, DataPlane, NoSources, [Hosting]);
        var before = services.ToList();
        var second = sameRoot ? DataPlane : ControlPlane;

        var failure = Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(services, second, NoSources, []));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics!.Values, pair => pair.Key == "module" && pair.Value == second.Module);
        Assert.Equal(before, services);
    }

    [Fact]
    public void ARootAfterAModuleOfItsNameFails()
    {
        var services = new ServiceCollection();
        CompositionServices.AddModule(services, new CompositionModule("control-plane", [], []));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(services, ControlPlane, NoSources, [])).Code);
    }

    [Fact]
    public void NullArgumentsFail()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(null!, DataPlane, NoSources, [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(new ServiceCollection(), null!, NoSources, [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(new ServiceCollection(), DataPlane, null!, [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddRoot(new ServiceCollection(), DataPlane, NoSources, null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddModule(null!, new CompositionModule("a", [], []))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionServices.AddModule(new ServiceCollection(), null!)).Code);
    }

    [Fact]
    public void AModuleIsAddedWithOneCheckPerCollection()
    {
        var services = new ServiceCollection();
        var first = new CompositionModule("coordination", [], ["hosting"]);

        CompositionServices.AddModule(services, first);
        CompositionServices.AddModule(services, new CompositionModule("container", [], ["hosting"]));

        Assert.Same(first, services.First(descriptor => descriptor.ServiceType == typeof(CompositionModule)).ImplementationInstance);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CompositionCheck));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void ARepeatedModuleFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        CompositionServices.AddModule(services, new CompositionModule("coordination", [], []));
        var before = services.ToList();

        var failure = Assert.Throws<GatewayException>(() => CompositionServices.AddModule(services, new CompositionModule("coordination", [], [])));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Equal(before, services);
    }
}

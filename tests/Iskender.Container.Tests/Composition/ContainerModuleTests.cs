using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Container.Tests.Composition;

/// <summary><c>AddIskenderContainer()</c>'s registrations (hosting contract, Registration rule 6 and Lifetimes rule 2).</summary>
public class ContainerModuleTests
{
    [Fact]
    public void TheModuleBuildsWithScopeAndBuildValidation()
    {
        var services = new ServiceCollection();
        TestPlanes.Root(services, new Domain.Registry.Composition.CompositionSources(null, null, false, null));
        services.AddIskenderContainer();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Same(provider.GetRequiredService<ContainerLifecycle>(), provider.GetServices<IHostedService>().OfType<ContainerLifecycle>().Single());
        Assert.Equal("starting", provider.GetRequiredService<ContainerLifecycle>().Phase);
    }

    [Fact]
    public async Task WithoutACompositionRootTheHostFailsWithRegistrationMissing()
    {
        var log = new CapturedLog();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(new Microsoft.Extensions.Logging.LoggerFactory([log]));
        builder.Services.AddIskenderContainer();
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Single(log.ById(6003));
    }

    [Fact]
    public void ASecondCallIsADuplicate()
    {
        var services = new ServiceCollection().AddIskenderContainer();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderContainer()).Code);
    }

    [Fact]
    public void ANullCollectionIsRejected() =>
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ContainerServiceCollectionExtensions.AddIskenderContainer(null!)).Code);
}

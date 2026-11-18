using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Work.Tests.Registration;

/// <summary>
/// The two registration methods (ISK-0009 T018, <c>work</c> contract Registration rules 1 and 2, Leases and fencing rule
/// 1, FR-023, FR-037): the dispatcher's module, section, requirements, feature, and start-up, and that the outbox's
/// registration alone declares no feature and no section.
/// </summary>
public class DispatcherRegistrationTests
{
    [Fact]
    public void TheDispatcherRegistersItsModuleSectionRequirementsFeatureAndStartup()
    {
        var services = new ServiceCollection().AddIskenderPostgreSqlWorkDispatcher();

        var module = Assert.Single(Instances<CompositionModule>(services));
        Assert.Equal("work-dispatcher", module.Name);
        Assert.Equal(["work"], module.Sections.Select(section => section.Namespace));
        Assert.Equal(["work-postgresql", "coordination-postgresql"], module.Requires);

        var feature = Assert.Single(Instances<FeatureRequirement>(services));
        Assert.Equal(("work.dispatch", "work", true, OutageBehavior.FailClosed), (feature.Name, feature.Module, feature.MandatoryPath, feature.Outage));
        Assert.Equal(
            [(CoordinationPrimitive.Leases, ConsistencyLevel.Strict), (CoordinationPrimitive.Fencing, ConsistencyLevel.Strict)],
            feature.Minimums.OrderBy(minimum => minimum.Key).Select(minimum => (minimum.Key, minimum.Value)));

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ICompositionStartup));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkSettings));
    }

    [Fact]
    public void TheOutboxRegistrationAloneDeclaresNoFeatureAndNoSection()
    {
        var services = new ServiceCollection().AddIskenderPostgreSqlWork();

        var module = Assert.Single(Instances<CompositionModule>(services));
        Assert.Equal("work-postgresql", module.Name);
        Assert.Equal(["persistence-postgresql"], module.Requires);
        Assert.Empty(module.Sections);
        Assert.Empty(Instances<FeatureRequirement>(services));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICompositionStartup));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkOutbox));
        Assert.Equal(["directory", "work"], Instances<MigrationModule>(services).Select(migration => migration.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASecondCallOfEitherFailsWithRegistrationDuplicate()
    {
        var services = new ServiceCollection().AddIskenderPostgreSqlWork().AddIskenderPostgreSqlWorkDispatcher();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlWork()).Code);
        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlWorkDispatcher()).Code);
    }

    private static IEnumerable<T> Instances<T>(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(T) && descriptor.ImplementationInstance is T).Select(descriptor => (T)descriptor.ImplementationInstance!);
}

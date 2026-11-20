using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.PostgreSql.Tests.Composition;

/// <summary>The PostgreSQL registrations' modules, sections, and requirements, without a container (research R4).</summary>
public class PostgreSqlRegistrationTests
{
    [Fact]
    public void PersistenceComposesItsModuleWithThePersistenceSection()
    {
        var services = new ServiceCollection();

        services.AddIskenderPostgreSqlPersistence();

        var module = Assert.Single(Modules(services));
        Assert.Equal("persistence-postgresql", module.Name);
        Assert.Equal(["persistence"], module.Sections.Select(section => section.Namespace));
        Assert.Equal(["hosting"], module.Requires);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IUnitOfWorkFactory));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMigrationRunner));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPersistenceHealthProbe));
    }

    [Fact]
    public void PersistenceAloneRegistersNoCoordinatorAndNoCoordinationSchema()
    {
        var services = new ServiceCollection();

        services.AddIskenderPostgreSqlPersistence();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CoordinatorRegistration));
        Assert.DoesNotContain(Modules(services).SelectMany(module => module.Sections), section => section.Namespace == "coordination");
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ILeaseCoordinator));
    }

    [Fact]
    public void CoordinationComposesAPostgreSqlCoordinatorRequiringCoordinationAndPersistence()
    {
        var services = new ServiceCollection();

        services.AddIskenderPostgreSqlCoordination();

        var module = Assert.Single(Modules(services));
        Assert.Equal("coordination-postgresql", module.Name);
        Assert.Empty(module.Sections);
        Assert.Equal(["coordination", "persistence-postgresql"], module.Requires);
        var registration = Assert.IsType<CoordinatorRegistration>(Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoordinatorRegistration)).ImplementationInstance);
        Assert.Equal(CoordinatorKind.PostgreSql, registration.Kind);
        Assert.Equal(CoordinatorCapabilities.PostgreSql, registration.Capabilities);
        Assert.NotNull(registration.Atomic);
        Assert.NotNull(registration.Leases);
        Assert.NotNull(registration.Fencing);
        Assert.NotNull(registration.Cache);
        Assert.Contains(services, descriptor => ReferenceEquals(descriptor.ImplementationInstance, CoordinationMigrations.Module));
        Assert.Single(services, descriptor => ReferenceEquals(descriptor.ImplementationInstance, DirectoryMigrations.Module));
        Assert.Empty(CoordinationMigrations.Module.DependsOn);
    }

    // The coordinator registers scopes in the directory and the outbox writes it, so either adds the module, and both add it
    // once, since a migration set refuses a module twice (ISK-0009 T039, persistence contract Migrations rule 12).
    [Fact]
    public void CoordinationAndWorkTogetherAddTheDirectoryModuleOnce()
    {
        var services = new ServiceCollection();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderPostgreSqlWork();

        Assert.Single(services, descriptor => ReferenceEquals(descriptor.ImplementationInstance, DirectoryMigrations.Module));
        _ = new MigrationSet(services.Where(descriptor => descriptor.ImplementationInstance is MigrationModule).Select(descriptor => (MigrationModule)descriptor.ImplementationInstance!));
    }

    [Fact]
    public void ARepeatOfEitherFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        var before = services.ToList();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlPersistence()).Code);
        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlCoordination()).Code);
        Assert.Equal(before, services);
    }

    [Fact]
    public void ASecondPostgreSqlCoordinatorFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        CoordinatorRegistration.Add(services, new CoordinatorRegistration(CoordinatorKind.PostgreSql, CoordinatorCapabilities.PostgreSql, _ => throw new InvalidOperationException()));
        var before = services.ToList();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlCoordination()).Code);
        Assert.Equal(before, services);
    }

    private static IEnumerable<CompositionModule> Modules(ServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(CompositionModule)).Select(descriptor => (CompositionModule)descriptor.ImplementationInstance!);
}

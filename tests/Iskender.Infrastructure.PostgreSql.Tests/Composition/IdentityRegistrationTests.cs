using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.PostgreSql.Tests.Composition;

/// <summary>
/// <c>AddIskenderPostgreSqlIdentity()</c> (ISK-0010 T016, operator-security contract Composition rule 2, hosting contract
/// Registration rules 6, 7, and 9): its module and requirements, its services and migration modules, a second call, and a
/// host without <c>AddIskenderPostgreSqlWork()</c>.
/// </summary>
public class IdentityRegistrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItComposesItsModuleRequiringPersistenceAndWorkWithNoSection()
    {
        var services = new ServiceCollection();

        services.AddIskenderPostgreSqlIdentity();

        var module = Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(CompositionModule)).Select(descriptor => (CompositionModule)descriptor.ImplementationInstance!));
        Assert.Equal("identity-postgresql", module.Name);
        Assert.Empty(module.Sections);
        Assert.Equal(["persistence-postgresql", "work-postgresql"], module.Requires);
        foreach (var service in new[] { typeof(IInstallationState), typeof(IOperatorTokens), typeof(IOperatorRedemption), typeof(IOperatorAuthorization), typeof(IEnrollmentCodes), typeof(IAuditRecorder) })
        {
            Assert.Single(services, descriptor => descriptor.ServiceType == service && descriptor.Lifetime == ServiceLifetime.Singleton);
        }

        Assert.Single(services, descriptor => ReferenceEquals(descriptor.ImplementationInstance, AuditMigrations.Module));
        Assert.Single(services, descriptor => ReferenceEquals(descriptor.ImplementationInstance, IdentityMigrations.Module));
    }

    [Fact]
    public void WithTheWorkModulesTheMigrationSetIsInItsForwardOrder()
    {
        var services = new ServiceCollection();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderPostgreSqlWork();
        services.AddIskenderPostgreSqlIdentity();

        var set = new MigrationSet(services.Where(descriptor => descriptor.ImplementationInstance is MigrationModule).Select(descriptor => (MigrationModule)descriptor.ImplementationInstance!));

        Assert.Equal(["coordination/1", "directory/1", "work/1", "audit/1", "identity/1"], set.Migrations.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public void ASecondCallFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        services.AddIskenderPostgreSqlIdentity();
        var before = services.ToList();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderPostgreSqlIdentity()).Code);
        Assert.Equal(before, services);
    }

    [Fact]
    public async Task WithoutTheWorkAdapterStartFailsWithRegistrationMissing()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(options =>
                {
                    options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{}}");
                    options.EnvironmentName = "development";
                });
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderPostgreSqlIdentity();
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Value == "work-postgresql");
    }
}

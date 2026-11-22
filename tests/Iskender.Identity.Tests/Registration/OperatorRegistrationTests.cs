using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Management;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Registration;

/// <summary>
/// <c>AddIskenderOperatorSecurity()</c> (ISK-0010 T017, operator-security contract Composition rules 1 and 5, hosting
/// contract Registration rules 6 and 7, FR-030, FR-033): its module, section, and requirements; a second call; a start
/// under the data-plane or worker root; and those planes refusing an <c>operator</c> section.
/// </summary>
public class OperatorRegistrationTests(OperatorFixture operators) : IClassFixture<OperatorFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItComposesItsModuleWithTheOperatorSectionRequiringTheControlPlaneAndIdentity()
    {
        var services = new ServiceCollection();

        services.AddIskenderOperatorSecurity();

        var module = Assert.Single(CompositionModules.Modules(services));
        Assert.Equal("operator-security", module.Name);
        Assert.Equal(["operator"], module.Sections.Select(section => section.Namespace));
        Assert.Same(OperatorSectionSchema.Schema, Assert.Single(module.Sections));
        Assert.Equal(["control-plane", "identity-postgresql"], module.Requires);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(System.Threading.RateLimiting.FixedWindowRateLimiter) && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void ASecondCallFailsBeforeAnythingChanges()
    {
        var services = new ServiceCollection();
        services.AddIskenderOperatorSecurity();
        var before = services.ToList();

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(() => services.AddIskenderOperatorSecurity()).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((IServiceCollection)null!).AddIskenderOperatorSecurity()).Code);
        Assert.Equal(before, services);
    }

    [Fact]
    public async Task TheControlPlaneHostStartsWithTheRegistrations()
    {
        operators.RequirePostgreSql();

        await using var host = await operators.HostAsync();

        Assert.Equal(10, host.Services.GetRequiredService<OperatorSettings>().AttemptsPerMinute);
    }

    public static TheoryData<string> OtherRoots() => ["data-plane", "worker"];

    [Theory]
    [MemberData(nameof(OtherRoots))]
    public async Task UnderAnotherRootStartFailsWithRegistrationMissing(string root)
    {
        operators.RequirePostgreSql();
        var document = OperatorHost.Document(operators, new OperatorHostOptions());

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                Root(builder.Services, root, document);
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderPostgreSqlWork();
                builder.Services.AddIskenderPostgreSqlIdentity();
                builder.Services.AddIskenderOperatorSecurity();
                builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(operators.Postgres));
            },
            app => app.MapIskenderOperatorSecurity("/admin/v1"),
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "missing" && value.Value == "control-plane");
    }

    [Theory]
    [MemberData(nameof(OtherRoots))]
    public async Task TheOtherPlanesRejectAnOperatorSection(string root)
    {
        operators.RequirePostgreSql();
        var document = OperatorHost.Document(operators, new OperatorHostOptions { OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":5}" });

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                Root(builder.Services, root, document);
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(operators.Postgres));
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.namespace.unknown at iskender.operator", failure.Error!.Diagnostics.Text, StringComparison.Ordinal);
    }

    private static void Root(IServiceCollection services, string root, string document)
    {
        if (root == "data-plane")
        {
            services.AddIskender(options =>
            {
                options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document);
                options.EnvironmentName = "development";
            });
        }
        else
        {
            services.AddIskenderWorker(options =>
            {
                options.BaseDocument = CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, document);
                options.EnvironmentName = "development";
            });
        }
    }
}

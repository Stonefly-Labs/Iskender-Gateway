using System.Text.Json.Nodes;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Container.Tests.Fixture.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CapturedLog = Iskender.Hosting.Testing.CapturedLog;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 2, acceptance scenario 7, for <c>single_instance_violated</c>: on ISK-0004's PostgreSQL fixture, a host whose
/// data-plane instance lease another host already holds refuses to start for a mandatory feature, and starts without
/// <c>MandatoryPath</c> (hosting contract, Start-up rule 7; research R4). The lease is verified only on PostgreSQL.
/// </summary>
public class MandatoryFeatureSingleInstanceTests(PlaneDatabase database) : IClassFixture<PlaneDatabase>
{
    private const string Reason = "single_instance_violated";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMandatoryFeatureFailsTheStartWhileAnotherHostHoldsTheLease()
    {
        await using var holder = await HolderAsync();

        var logs = new CapturedLog();
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                Compose(builder, mandatory: true);
            },
            ct: Token));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal(
            [new("feature", MandatoryFeatureRefusalTests.Feature), new("module", MandatoryFeatureRefusalTests.Module), new("reason", Reason)],
            failure.Error!.Diagnostics.Values);
        var refused = logs.Only(6004);
        Assert.Equal((MandatoryFeatureRefusalTests.Feature, MandatoryFeatureRefusalTests.Module, Reason), (refused.Value("Feature"), refused.Value("Module"), refused.Value("Reason")));
        Assert.Contains(logs.ById(5002), entry => entry.Value("Reason") == Reason);
        Assert.Equal(SingleInstanceState.Held, holder.Services.GetRequiredService<ISingleInstanceStatus>().State);
    }

    [Fact]
    public async Task WithoutMandatoryPathTheHostStartsAndTheFeatureIsRefused()
    {
        await using var holder = await HolderAsync();

        await using var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, mandatory: false), ct: Token);

        Assert.Empty(host.Logs.ById(6004));
        var capability = host.Services.GetRequiredService<CapabilityReport>().Find(MandatoryFeatureRefusalTests.Feature)!;
        Assert.Equal((false, Reason), (capability.Accepted, capability.Reason.ToCode()));
    }

    // The first host holds the data plane's lease once the coordination tables exist, which its own runner applies.
    private async Task<EmbeddedHost> HolderAsync()
    {
        database.RequirePostgreSql();
        var holder = await EmbeddedHost.CreateAsync(builder => Compose(builder, mandatory: null), start: false, ct: Token);
        try
        {
            await holder.Services.GetRequiredService<IMigrationRunner>().ApplyAsync(new MigrationApplyRequest(), Token);
            await holder.StartAsync(Token);
            Assert.Equal(SingleInstanceState.Held, holder.Services.GetRequiredService<ISingleInstanceStatus>().State);
            return holder;
        }
        catch
        {
            await holder.DisposeAsync();
            throw;
        }
    }

    private void Compose(WebApplicationBuilder builder, bool? mandatory)
    {
        var document = JsonNode.Parse(database.Document())!;
        document["iskender"]!["coordination"] = JsonNode.Parse("{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}}");
        builder.Services.AddIskender(options =>
        {
            options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document.ToJsonString());
            options.EnvironmentName = "development";
        });
        builder.Services.AddIskenderCoordination();
        builder.Services.AddIskenderPostgreSqlPersistence();
        builder.Services.AddIskenderPostgreSqlCoordination();
        builder.Services.AddSingleton<IDatabaseCredentialProvider>(database.Server.Credentials());
        if (mandatory is { } path)
        {
            builder.Services.AddSingleton(MandatoryFeatureRefusalTests.Guarded(CoordinationPrimitive.Leases, ConsistencyLevel.Leased, path));
        }
    }
}

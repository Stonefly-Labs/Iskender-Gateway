using System.Text.Json.Nodes;
using Iskender.Domain.Ports;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Infrastructure.PostgreSql.Tests.Hosting;

/// <summary>
/// User Story 1, acceptance scenario 5: an embedded host declaring <c>single_instance</c> with PostgreSQL leases takes the
/// data plane's instance lease at start, runs the capability check once, and releases the lease at stop. It needs the
/// PostgreSQL container, so it is skipped with its reason without one, and fails instead when <c>CI</c> is <c>true</c>.
/// </summary>
public class SingleInstanceHostTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHostHoldsTheInstanceLeaseWhileRunningAndReleasesItAtStop()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        var document = Document(database);

        var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(options =>
                {
                    options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document);
                    options.EnvironmentName = "development";
                });
                builder.Services.AddIskenderCoordination();
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderPostgreSqlCoordination();
                builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(fixture));
                builder.Services.AddSingleton(Feature("host.leased", CoordinationPrimitive.Leases, ConsistencyLevel.Strict));
                builder.Services.AddSingleton(Feature("host.cached", CoordinationPrimitive.CacheInvalidation, ConsistencyLevel.Strict));
            },
            ct: Token);
        await using (host)
        {
            Assert.Single(host.Logs.ById(5001));
            Assert.Single(host.Logs.ById(5002));
            Assert.Equal(SingleInstanceState.Held, host.Services.GetRequiredService<ISingleInstanceStatus>().State);
            Assert.Equal(1, await HeldLeasesAsync(database));

            await host.StopAsync(Token);

            Assert.Equal(0, await HeldLeasesAsync(database));
            Assert.Equal(SingleInstanceState.Held, host.Services.GetRequiredService<CapabilityReport>().SingleInstance);

            // A timer still running would take the lease again within one renewal period, a third of the 6s duration.
            await Task.Delay(TimeSpan.FromSeconds(3), Token);
            Assert.Equal(0, await HeldLeasesAsync(database));
        }
    }

    // The persistence section the fixture builds for this database, with the hosting and coordination sections beside it.
    private static string Document(ClassDatabase database)
    {
        var document = JsonNode.Parse(TestSettings.Document(database, withMigration: false))!;
        document["iskender"]!["coordination"] = new JsonObject
        {
            ["topology"] = "single_instance",
            ["leases"] = new JsonObject { ["coordinator"] = "postgresql" },
            ["instanceLease"] = new JsonObject { ["duration"] = "6s" },
        };
        return document.ToJsonString();
    }

    private static FeatureRequirement Feature(string name, CoordinationPrimitive primitive, ConsistencyLevel minimum) =>
        new(name, "coordination", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum }, mandatoryPath: false, OutageBehavior.FailClosed);

    private Task<long> HeldLeasesAsync(ClassDatabase database) =>
        fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_coordination.leases WHERE expires_at > clock_timestamp()");
}

using System.Text.Json.Nodes;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Work.Tests.Fixture;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Work.Tests.Dispatch;

/// <summary>
/// The dispatcher's refusal (ISK-0009 T031, US2.5 in process, FR-023, TR-12, as amended in <c>implement</c>): with leases
/// on Redis or the local substitute, a composition with the dispatcher is refused at start with
/// <c>unsupported_consistency</c> for <c>work.dispatch</c>, written as events 6004 and 5002, and dispatches nothing; one
/// that only enqueues is never refused. The exit-4 container test, with event 7002, is T051's.
/// </summary>
public class RefusalTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const string Redis = "\"redis\":{\"endpoints\":[\"cache-1.internal:6379\"],\"username\":\"isk_coordination\",\"password\":{\"secretRef\":\"redis/coordination\"}}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Coordinators() => ["redis", "local"];

    [Theory]
    [MemberData(nameof(Coordinators))]
    public async Task TheDispatcherIsRefusedWithUnsupportedConsistencyAndDispatchesNothing(string coordinator)
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "refused-" + coordinator);
        var logs = new Iskender.Hosting.Testing.CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                Compose(builder, coordinator, dispatcher: true);
            },
            ct: Token));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal([new("feature", "work.dispatch"), new("module", "work"), new("reason", "unsupported_consistency")], failure.Error!.Diagnostics.Values);
        var refused = logs.Only(6004);
        Assert.Equal(("work.dispatch", "work", "unsupported_consistency"), (refused.Value("Feature"), refused.Value("Module"), refused.Value("Reason")));
        Assert.Contains(logs.ById(5002), entry => entry.Value("Feature") == "work.dispatch" && entry.Value("Reason") == "unsupported_consistency");
        Assert.Empty(logs.ById(8001));
        Assert.Equal("pending|0", await work.ItemAsync(item));
    }

    [Theory]
    [MemberData(nameof(Coordinators))]
    public async Task ACompositionThatOnlyEnqueuesIsNeverRefused(string coordinator)
    {
        work.RequirePostgreSql();

        await using var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, coordinator, dispatcher: false), ct: Token);

        Assert.Empty(host.Logs.ById(6004));
        Assert.Null(host.Services.GetRequiredService<CapabilityReport>().Find("work.dispatch"));
        Assert.NotNull(host.Services.GetRequiredService<IWorkOutbox>());
    }

    [Fact]
    public async Task WithLeasesOnPostgreSqlTheDispatcherStartsAndCompletesAnItem()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "accepted");

        await using (var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                Compose(builder, "postgresql", dispatcher: true);
                WorkRegistrations.AddHandler<ApplyRecordingHandler>(builder.Services, ApplyRecordingHandler.Registration());
            },
            ct: Token))
        {
            Assert.True(host.Services.GetRequiredService<CapabilityReport>().Find("work.dispatch")!.Accepted);
            await Eventually.StateAsync(work, item, "completed");
            Assert.Single(host.Logs.ById(8001));
        }

        Assert.Equal(1L, await work.AppliesAsync(item));
    }

    private void Compose(WebApplicationBuilder builder, string coordinator, bool dispatcher)
    {
        var document = JsonNode.Parse(Iskender.Infrastructure.PostgreSql.Tests.Fixture.TestSettings.Document(work.Database))!;
        document["iskender"]!["coordination"] = JsonNode.Parse(
            "{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"" + coordinator + "\"}" + (coordinator == "redis" ? "," + Redis : "") + "}");
        builder.Services.AddIskender(options =>
        {
            options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document.ToJsonString());
            options.EnvironmentName = "development";
        });
        builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(work.Postgres));
        builder.Services.AddIskenderPostgreSqlPersistence();
        builder.Services.AddIskenderCoordination();
        builder.Services.AddIskenderPostgreSqlCoordination();
        builder.Services.AddIskenderRedisCoordination();
        builder.Services.AddIskenderPostgreSqlWork();
        if (dispatcher)
        {
            builder.Services.AddIskenderPostgreSqlWorkDispatcher();
        }
    }
}

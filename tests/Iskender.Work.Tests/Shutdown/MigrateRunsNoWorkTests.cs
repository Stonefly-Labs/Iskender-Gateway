using System.Text.Json.Nodes;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Iskender.Management.Migrations;
using Iskender.Work.Tests.Fixture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CapturedLog = Iskender.Hosting.Testing.CapturedLog;

namespace Iskender.Work.Tests.Shutdown;

/// <summary>
/// FR-028, <c>work</c> contract Shutdown rule 5, container contract Migration command rule 3 (ISK-0009 T050): a
/// <c>migrate</c>-shaped run, which builds the container and runs one migration operation but no composition start-up,
/// dispatches nothing and runs no job, even over the worker's registrations with a due item and due jobs.
/// </summary>
public class MigrateRunsNoWorkTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMigrateRunOverTheWorkersRegistrationsDispatchesNothingAndRunsNoJob()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "migrate");
        await work.ExecuteAsync("DELETE FROM isk_work.job_runs");
        var logs = new CapturedLog();

        MigrationOutcome outcome;
        await using (var services = Compose(logs).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
        {
            outcome = await MigrationCommand.RunAsync(services, new MigrationOperation.Apply(), Token);
            Assert.NotEqual(CompositionPhase.Started, services.GetRequiredService<CompositionStatus>().Phase);
        }

        Assert.Equal(("apply", "succeeded", 0, (Iskender.Core.Contracts.Errors.ErrorCode?)null), (outcome.Operation, outcome.Outcome, outcome.Count, outcome.Code));
        Assert.Equal("pending|0", await work.ItemAsync(item));
        Assert.Equal(0L, await work.AppliesAsync(item));
        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_work.job_runs"));
        Assert.Empty(logs.ById(8001));
        Assert.Empty(logs.ById(8011));
    }

    // The worker's registrations, as its serving command composes them, over the class database.
    private ServiceCollection Compose(CapturedLog logs)
    {
        var document = JsonNode.Parse(Iskender.Infrastructure.PostgreSql.Tests.Fixture.TestSettings.Document(work.Database))!;
        document["iskender"]!["coordination"] = JsonNode.Parse("{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}}");
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));
        services.AddIskenderWorker(options =>
        {
            options.BaseDocument = CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, document.ToJsonString());
            options.EnvironmentName = "development";
        });
        services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(work.Postgres));
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
        services.AddIskenderPostgreSqlWork();
        services.AddIskenderPostgreSqlWorkDispatcher();
        WorkRegistrations.AddHandler<ApplyRecordingHandler>(services, ApplyRecordingHandler.Registration());

        // The handler's own test module, which the class database also holds, so the set matches and nothing is pending.
        services.AddSingleton(AppliesModule.Module);
        return services;
    }
}

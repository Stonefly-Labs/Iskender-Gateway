using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Management.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CapturedLog = Iskender.Hosting.Testing.CapturedLog;

namespace Iskender.Container.Tests.Migrations;

/// <summary>
/// User Story 4, acceptance scenarios 1, 2, and 6, on ISK-0004's PostgreSQL fixture: <see cref="MigrationCommand"/> run
/// against the control plane's own registrations, with a test credential provider and the migration login (container
/// contract, Migration command rules 2 to 6). Each test has a database of its own, and every run starts nothing: no
/// composition start-up, no capability check, no coordinator, and no listener (rule 3).
/// </summary>
public class MigrationCommandTests(PostgreSqlFixture fixture)
{
    private static readonly int[] NotStarted = [5001, 5002, 5011, 7001, 7002, 7003, 7004, 7005];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyOnAnEmptyDatabaseAppliesCoordinationAndExits0()
    {
        await using var database = await DatabaseAsync();
        var run = new Run(database);

        var outcome = await run.CommandAsync(new MigrationOperation.Apply());

        Assert.Equal((ExitCodes.Success, "apply", "succeeded", 1, (ErrorCode?)null), (outcome.ExitCode, outcome.Operation, outcome.Outcome, outcome.Count, outcome.Code));
        Assert.Equal("coordination/1", await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(module || '/' || version, ',') FROM isk_migrations.history"));
        Assert.Single(run.Log.ById(4007));
        Assert.NotEmpty(run.Log.ById(4009));
        Assert.Single(run.Log.ById(4011));
        await run.AssertNothingStartedAsync();
    }

    [Fact]
    public async Task ReverseOfADataLosingStepNeedsConfirmation()
    {
        await using var database = await DatabaseAsync();
        await new Run(database).CommandAsync(new MigrationOperation.Apply());

        var refused = await new Run(database).CommandAsync(new MigrationOperation.Reverse(1, ConfirmDataLoss: false));

        Assert.Equal((ExitCodes.MigrationFailed, "failed", ErrorCodes.PersistenceReverseRequiresConfirmation), (refused.ExitCode, refused.Outcome, refused.Code));
        Assert.Equal(1L, await HistoryCountAsync(database));

        var run = new Run(database);
        var reversed = await run.CommandAsync(new MigrationOperation.Reverse(1, ConfirmDataLoss: true));

        Assert.Equal((ExitCodes.Success, "reverse", "succeeded", 1), (reversed.ExitCode, reversed.Operation, reversed.Outcome, reversed.Count));
        Assert.Equal(0L, await HistoryCountAsync(database));
        await run.AssertNothingStartedAsync();
    }

    [Fact]
    public async Task StatusIsBehindBeforeApplyAndCurrentAfter()
    {
        await using var database = await DatabaseAsync();

        var before = await new Run(database).CommandAsync(new MigrationOperation.Status());
        await new Run(database).CommandAsync(new MigrationOperation.Apply());
        var run = new Run(database);
        var after = await run.CommandAsync(new MigrationOperation.Status());

        Assert.Equal((ExitCodes.SchemaBehind, "status", "behind"), (before.ExitCode, before.Operation, before.Outcome));
        Assert.Equal((ExitCodes.Success, "status", "current"), (after.ExitCode, after.Operation, after.Outcome));
        await run.AssertNothingStartedAsync();
    }

    [Theory]
    [InlineData("INSERT INTO isk_migrations.history (module, version, checksum, applied_at, applied_by) VALUES ('coordination', 99, 'planted', now(), 'test')")]
    [InlineData("UPDATE isk_migrations.history SET checksum = 'changed' WHERE module = 'coordination' AND version = 1")]
    public async Task StatusIsAheadWithAnUnknownMigrationOrAChangedChecksum(string plant)
    {
        await using var database = await DatabaseAsync();
        await new Run(database).CommandAsync(new MigrationOperation.Apply());
        await fixture.ScalarAsync<object?>(database.Name, plant + " RETURNING 1");

        var outcome = await new Run(database).CommandAsync(new MigrationOperation.Status());

        Assert.Equal((ExitCodes.SchemaAhead, "ahead"), (outcome.ExitCode, outcome.Outcome));
    }

    [Fact]
    public async Task AStatusThatCannotConnectExits5()
    {
        await using var database = await DatabaseAsync();
        var run = new Run(database, new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = "iskender-database.invalid", ["connectionTimeout"] = "1s" });

        var outcome = await run.CommandAsync(new MigrationOperation.Status());

        Assert.Equal((ExitCodes.MigrationFailed, "status", "failed"), (outcome.ExitCode, outcome.Operation, outcome.Outcome));
        Assert.StartsWith("persistence.", outcome.Code!.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingMigrationLoginExits5BeforeConnecting()
    {
        await using var database = await DatabaseAsync();
        var run = new Run(database, withMigration: false);

        var outcome = await run.CommandAsync(new MigrationOperation.Apply());

        Assert.Equal((ExitCodes.MigrationFailed, "failed", ErrorCodes.PersistenceUnavailable), (outcome.ExitCode, outcome.Outcome, outcome.Code));
        Assert.DoesNotContain(run.Credentials.Requests, request => request.Reference == TestSettings.MigrationSecret);
        Assert.Empty(run.Log.ById(4007));
        Assert.False(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_migrations.history') IS NOT NULL"));
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public async Task WithNoCredentialProviderEveryOperationExits5WithPersistenceUnavailable(string name)
    {
        await using var database = await DatabaseAsync();
        MigrationOperation operation = name == "apply" ? new MigrationOperation.Apply() : new MigrationOperation.Status();

        var outcome = await new Run(database, withCredentials: false).CommandAsync(operation);

        Assert.Equal((ExitCodes.MigrationFailed, "failed", ErrorCodes.PersistenceUnavailable), (outcome.ExitCode, outcome.Outcome, outcome.Code));
    }

    [Fact]
    public async Task OfTwoConcurrentAppliesTheOneWhoseWaitEndsFirstExits5Locked()
    {
        await using var database = await DatabaseAsync();
        var patient = new Run(database, TestSettings.MigrationWithLockWait("30s"));
        var impatient = new Run(database, TestSettings.MigrationWithLockWait("1s"));
        Task<MigrationOutcome> waiting;
        await using (var holder = await fixture.OpenSuperuserAsync(database.Name))
        {
            await using (var take = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key})", holder))
            {
                await take.ExecuteScalarAsync(Token);
            }

            waiting = patient.CommandAsync(new MigrationOperation.Apply());
            var locked = await impatient.CommandAsync(new MigrationOperation.Apply());

            Assert.Equal((ExitCodes.MigrationFailed, "failed", ErrorCodes.PersistenceMigrationLocked), (locked.ExitCode, locked.Outcome, locked.Code));
            Assert.False(waiting.IsCompleted);
        }

        // Closing the holder's session releases the lock, so the patient job takes it and applies.
        var applied = await waiting;
        Assert.Equal((ExitCodes.Success, 1), (applied.ExitCode, applied.Count));
    }

    private async Task<ScratchDatabase> DatabaseAsync()
    {
        fixture.RequirePostgreSql();
        return await ScratchDatabase.CreateAsync(fixture);
    }

    private static Task<long> HistoryCountAsync(ScratchDatabase database) =>
        database.Fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history");

    /// <summary>
    /// One <c>migrate</c> run: the control plane's registrations composed into a <see cref="ServiceCollection"/> with
    /// <c>ValidateOnBuild</c> and <c>ValidateScopes</c>, a captured log, a test credential provider, a recording start-up,
    /// and a document naming the fixture's database, a PostgreSQL lease coordinator, and the single-instance topology,
    /// so a start-up that ran would take a lease.
    /// </summary>
    private sealed class Run(ClassDatabase database, IReadOnlyDictionary<string, object>? overrides = null, bool withMigration = true, bool withCredentials = true)
    {
        public CapturedLog Log { get; } = new();

        public TestCredentialProvider Credentials { get; } = TestCredentialProvider.For(database.Fixture);

        public RecordingStartup Startup { get; } = new();

        public async Task<MigrationOutcome> CommandAsync(MigrationOperation operation)
        {
            var document = JsonNode.Parse(TestSettings.Document(database, overrides, withMigration))!;
            document["iskender"]!["container"] = JsonNode.Parse("{}");
            document["iskender"]!["coordination"] = JsonNode.Parse("{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}}");
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(LoggerFactory.Create(logging => logging.AddProvider(Log)));
            PlaneDefinitions.ControlPlane(also =>
            {
                if (withCredentials)
                {
                    also.AddSingleton<IDatabaseCredentialProvider>(Credentials);
                }
                also.AddSingleton<ICompositionStartup>(Startup);
            }).Compose(
                services,
                new CompositionSources(CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, document.ToJsonString()), null, false, "development"));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            return await MigrationCommand.RunAsync(provider, operation, Token);
        }

        public async Task AssertNothingStartedAsync()
        {
            Assert.Equal(0, Startup.Started);
            Assert.All(NotStarted, id => Assert.Empty(Log.ById(id)));
            // A reversed schema has no leases table, and so no lease.
            if (await database.Fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_coordination.leases') IS NOT NULL"))
            {
                Assert.Equal(0L, await database.Fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_coordination.leases"));
            }
        }
    }

    private sealed class RecordingStartup : ICompositionStartup
    {
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public string ModuleName => "control-plane";

        public ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

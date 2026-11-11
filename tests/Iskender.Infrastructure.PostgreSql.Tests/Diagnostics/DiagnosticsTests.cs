using System.Net;
using System.Net.Sockets;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Diagnostics;

/// <summary>
/// Every persistence event, activity, and metric is emitted with only the contract's allowed fields and labels (FR-032,
/// contract Diagnostics). One test drives each event 4001 to 4014 for real, so a missing event fails here.
/// </summary>
public class DiagnosticsTests(PostgreSqlFixture fixture)
{
    // Module, migration version, operation kind, outcome, failure class, reason code, durations, and the SQLSTATE class of 4014.
    private static readonly string[] AllowedFields = ["Module", "Version", "Operation", "Outcome", "FailureClass", "Reason", "PreviousReason", "DurationMs", "SqlStateClass"];

    private static readonly string[] AllowedLabels = [PersistenceTelemetry.ModuleTag, PersistenceTelemetry.OperationTag, PersistenceTelemetry.OutcomeTag, PersistenceTelemetry.FailureClassTag];

    private static readonly string[] Metrics =
    [
        "iskender.persistence.pool.connections",
        "iskender.persistence.connection.acquire_duration",
        "iskender.persistence.unit_of_work.duration",
        "iskender.persistence.conflicts",
        "iskender.persistence.retries",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryEventActivityAndMetricIsEmittedWithAllowedFieldsOnly()
    {
        fixture.RequirePostgreSql();
        var logged = new List<CapturedLog>();
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);

        logged.AddRange(await ConnectionFailureAsync(database));
        await using (var store = TestStore.For(database))
        {
            await UnitEventsAsync(store);
            logged.AddRange(store.Logger.Events);
        }

        await using (var empty = await ScratchDatabase.CreateAsync(fixture))
        {
            logged.AddRange(await MigrationEventsAsync(empty));
        }

        Assert.Equal(Enumerable.Range(4001, 14), logged.Select(entry => entry.Id.Id).Distinct().Order());
        Assert.All(logged, entry =>
        {
            Assert.NotNull(entry.Id.Name);
            Assert.All(entry.Fields, field => Assert.Contains(field.Key, AllowedFields));
            Assert.Null(entry.Exception);
        });
        // The division by zero, and the failing step's own unclassified failure: a two-character class only.
        var sqlStateClasses = logged.Where(entry => entry.Id.Id == 4014).Select(entry => (string?)entry.Fields.Single(field => field.Key == "SqlStateClass").Value).ToList();
        Assert.All(sqlStateClasses, value => Assert.Matches("^[0-9A-Z]{2}$", value!));
        Assert.Contains("22", sqlStateClasses);

        CapturingDiagnostics.Shared.Observe();
        var activities = CapturingDiagnostics.Shared.Activities.Select(activity => activity.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("persistence.unit_of_work", activities);
        Assert.Contains("persistence.migration_step", activities);
        var measurements = CapturingDiagnostics.Shared.Measurements.ToList();
        Assert.All(Metrics, metric => Assert.Contains(measurements, measurement => measurement.Instrument == metric));
        Assert.All(measurements, measurement => Assert.All(measurement.Labels, label => Assert.Contains(label.Key, AllowedLabels)));
    }

    // 4001: a unit cannot connect to a closed local port.
    private static async Task<IReadOnlyList<CapturedLog>> ConnectionFailureAsync(ClassDatabase database)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await using var store = TestStore.For(database, new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = "127.0.0.1", ["port"] = port });

        Assert.Same(ErrorCodes.PersistenceUnavailable, (await Assert.ThrowsAsync<GatewayException>(async () => await store.OpenAsync(Owners.Organization(Owners.OrgA)))).Code);
        return store.Logger.Events;
    }

    // 4002 to 4006, 4013, and 4014, with commits, a conflict, and a retry for the metrics.
    private static async Task UnitEventsAsync(TestStore store)
    {
        var org = Owners.Organization(Owners.OrgA);
        var id = $"w-diag-{Guid.NewGuid():N}";
        await using (var unit = await store.OpenAsync(org))
        {
            await WidgetRepository.InsertAsync(unit, id, "diagnostics", Token);
            await unit.CommitAsync(Token);
        }

        await using (await store.OpenAsync(org))
        {
            // 4002: disposed without a commit.
        }

        await using (var unit = await store.OpenAsync(org))
        {
            // 4003, and a conflict: the key is taken.
            Assert.Same(ErrorCodes.PersistenceUniqueConflict, (await Assert.ThrowsAsync<GatewayException>(() => WidgetRepository.InsertAsync(unit, id, "again", Token))).Code);
        }

        await using (var unit = await store.OpenAsync(org))
        {
            // 4004: the connection breaks during COMMIT.
            PostgreSqlUnitOfWork.From(unit).DuringCommit = () => throw new NpgsqlException("Exception while reading from stream", new IOException("connection broken"));
            Assert.Same(ErrorCodes.PersistenceUnavailable, (await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token))).Code);
        }

        // 4005: a serialization failure on the first attempt, then success.
        var attempts = 0;
        await UnitOfWorkRetry.RunAsync(
            store.Factory,
            org,
            TestStore.Options("diag.retry") with { SafeToRerun = true },
            async (unit, token) =>
            {
                if (++attempts == 1)
                {
                    await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("DO $$ BEGIN RAISE EXCEPTION USING ERRCODE = '40001'; END $$").ExecuteAsync(token);
                }

                return true;
            },
            Token,
            TimeProvider.System,
            store.Logger);

        // 4006: a bounded wait on a lock another unit in the same scope holds.
        var name = new AdvisoryLockName("beta", "diag." + Guid.NewGuid().ToString("N")[..12]);
        await using (var holder = await store.OpenAsync(org))
        await using (var waiter = await store.OpenAsync(org))
        {
            Assert.True(await holder.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
            Assert.Same(ErrorCodes.PersistenceLockTimeout, (await Assert.ThrowsAsync<GatewayException>(async () => await waiter.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.Bounded(TimeSpan.FromMilliseconds(100)), Token))).Code);
        }

        // 4014: a failure no class describes (division by zero, SQLSTATE class 22).
        await using (var unit = await store.OpenAsync(org))
        {
            Assert.Same(ErrorCodes.Internal, (await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT 1 / 0").ExecuteAsync(Token))).Code);
        }

        // 4013: the probe's first reason.
        Assert.Equal(PersistenceHealthResult.Healthy, await new PostgreSqlHealthProbe(store.Sources, FixtureModules.Release1, TimeProvider.System, store.Logger).CheckAsync(Token));
    }

    // 4007 to 4012: a failing step, a run that waits for the lock, and a refused set.
    private async Task<IReadOnlyList<CapturedLog>> MigrationEventsAsync(ClassDatabase database)
    {
        var logged = new List<CapturedLog>();
        await using (var failing = await ScratchDatabase.CreateAsync(fixture))
        await using (var failingStore = TestStore.For(failing))
        {
            // Its own database: the module's first step applies, and that history would make release 1 refuse.
            await Assert.ThrowsAsync<GatewayException>(async () => await failingStore.Runner(new MigrationSet([FixtureModules.ForwardFails])).ApplyAsync(new MigrationApplyRequest(), Token));
            logged.AddRange(failingStore.Logger.Events);
        }

        await using var store = TestStore.For(database, TestSettings.MigrationWithLockWait("30s"));
        await using var holder = await fixture.OpenSuperuserAsync(database.Name);
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key})", holder))
        {
            await take.ExecuteScalarAsync(Token);
        }

        var run = store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token).AsTask();
        while (!store.Logger.Events.Any(entry => entry.Id.Id == 4008))
        {
            Assert.False(run.IsCompleted, "The runner finished without waiting for the lock.");
            await Task.Delay(50, Token);
        }

        await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationLock.Key})", holder))
        {
            await release.ExecuteScalarAsync(Token);
        }

        Assert.Equal(6, (await run).Applied.Count);
        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, Assert.Throws<GatewayException>(() => new MigrationSet([FixtureModules.MissingReverse], store.Logger)).Code);
        logged.AddRange(store.Logger.Events);
        return logged;
    }
}

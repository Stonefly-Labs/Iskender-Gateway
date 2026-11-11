using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>User Story 1 scenarios 4 to 6 and the migration edge cases. Each test starts from its own empty database.</summary>
public class MigrationFailureTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFailingForwardScriptRollsBackAloneAndNamesOnlyItsModuleVersionAndReason()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([FixtureModules.ForwardFails])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(new GatewayException(ErrorCodes.Internal).Message, failure.Message);
        Assert.Null(failure.InnerException);
        Assert.Equal(Values(("module", "forward_fails"), ("version", "2"), ("reason", MigrationRefusal.ForwardFailed)), Diagnostics(failure));
        Assert.Null(failure.Error!.Diagnostics.Text);
        Assert.Equal(["forward_fails/1"], await HistoryAsync(database));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_forward_fails.first') IS NOT NULL AND to_regclass('isk_forward_fails.second') IS NULL"));
        var stepFailed = Assert.Single(store.Logger.Events, logged => logged.Id.Id == 4010);
        Assert.Contains(stepFailed.Fields, field => field.Key == "Reason" && (string?)field.Value == MigrationRefusal.ForwardFailed);
        Assert.All(store.Logger.Events, logged =>
        {
            Assert.DoesNotContain("division", logged.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT", logged.Message, StringComparison.Ordinal);
            Assert.Null(logged.Exception);
        });
    }

    [Fact]
    public async Task AFailingFirstMigrationLeavesNoModuleSchema()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([FixtureModules.FailsFirst])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Equal(Values(("module", "fails_first"), ("version", "1"), ("reason", MigrationRefusal.ForwardFailed)), Diagnostics(failure));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regnamespace('isk_fails_first') IS NULL"));
        Assert.Empty(await HistoryAsync(database));
    }

    [Fact]
    public async Task AScriptEditedAfterApplyStopsTheRunnerAndChangesNothing()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.AlphaRelease1])).ApplyAsync(new MigrationApplyRequest(), Token);
        await store.Runner(new MigrationSet([FixtureModules.AlphaRelease1])).ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token);
        var snapshot = await CatalogSnapshot.RenderAsync(fixture, database.Name);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([FixtureModules.AlphaEdited])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceMigrationChecksumMismatch, failure.Code);
        Assert.Equal(Values(("module", "alpha"), ("version", "1"), ("reason", MigrationRefusal.ChecksumMismatch)), Diagnostics(failure));
        Assert.Equal(["alpha/1", "alpha/2"], await HistoryAsync(database));
        Assert.Equal(snapshot, await CatalogSnapshot.RenderAsync(fixture, database.Name));
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4012);
    }

    [Fact]
    public async Task AnUnconfirmedDataLosingStepAnywhereInTheRequestRefusesItAllBeforeAnyStep()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(FixtureModules.Release1);
        await runner.ApplyAsync(new MigrationApplyRequest(), Token);
        var snapshot = await CatalogSnapshot.RenderAsync(fixture, database.Name);

        // beta/3, beta/2, beta/1, then alpha/3, the data-losing one, is the fourth step.
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest { Steps = 4 }, Token));

        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, failure.Code);
        Assert.Equal(Values(("module", "alpha"), ("version", "3")), Diagnostics(failure));
        Assert.Equal(6, (await HistoryAsync(database)).Count);
        Assert.Equal(snapshot, await CatalogSnapshot.RenderAsync(fixture, database.Name));
        Assert.DoesNotContain(store.Logger.Events, logged => logged.Id.Id == 4009 && logged.Fields.Any(field => field.Key == "Operation" && (string?)field.Value == "reverse"));

        Assert.Equal(4, (await runner.ReverseAsync(new MigrationReverseRequest { Steps = 4, ConfirmDataLoss = true }, Token)).Reversed.Count);
    }

    [Fact]
    public async Task AnAppliedMigrationTheReleaseDoesNotKnowStopsTheRunner()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(FixtureModules.Release2).ApplyAsync(new MigrationApplyRequest(), Token);

        var apply = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token));
        var reverse = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(FixtureModules.Release1).ReverseAsync(new MigrationReverseRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, apply.Code);
        Assert.Equal(Values(("module", "alpha"), ("version", "4"), ("reason", MigrationRefusal.UnknownAppliedMigration)), Diagnostics(apply));
        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, reverse.Code);
        Assert.Equal(7, (await HistoryAsync(database)).Count);
    }

    [Fact]
    public async Task ATenantTableWithNoPolicyIsRefusedAndNothingIsRecorded()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.Runner(new MigrationSet([FixtureModules.NoPolicy])).ApplyAsync(new MigrationApplyRequest(), Token));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, failure.Code);
        Assert.Equal(Values(("module", "no_policy"), ("version", "1"), ("reason", MigrationRefusal.MissingRowSecurity)), Diagnostics(failure));
        Assert.Empty(await HistoryAsync(database));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regnamespace('isk_no_policy') IS NULL"));
    }

    [Fact]
    public async Task ATerminatedLockHolderReleasesTheLockSoTheNextMigratorProceeds()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var holder = await fixture.OpenSuperuserAsync(database.Name);
        int pid;
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key}), pg_backend_pid()", holder))
        await using (var reader = await take.ExecuteReaderAsync(Token))
        {
            await reader.ReadAsync(Token);
            pid = reader.GetInt32(1);
        }

        await using var store = TestStore.For(database, TestSettings.MigrationWithLockWait("30s"));
        var apply = store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token).AsTask();
        await WaitForAsync(() => store.Logger.Events.Any(logged => logged.Id.Id == 4008));
        Assert.False(apply.IsCompleted);

        Assert.True(await fixture.ScalarAsync<bool>("postgres", $"SELECT pg_terminate_backend({pid})"));

        Assert.Equal(6, (await apply.WaitAsync(TimeSpan.FromSeconds(20), Token)).Applied.Count);
    }

    private async Task<List<string>> HistoryAsync(ClassDatabase database)
    {
        if (await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_migrations.history') IS NULL"))
        {
            return [];
        }

        await using var connection = await fixture.OpenSuperuserAsync(database.Name);
        await using var command = new NpgsqlCommand("SELECT module || '/' || version FROM isk_migrations.history ORDER BY module, version", connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var rows = new List<string>();
        while (await reader.ReadAsync(Token))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(50, Token);
        }

        Assert.True(condition());
    }

    private static Dictionary<string, string> Diagnostics(GatewayException failure) =>
        failure.Error!.Diagnostics.Values.ToDictionary(value => value.Key, value => value.Value);

    private static Dictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value);
}

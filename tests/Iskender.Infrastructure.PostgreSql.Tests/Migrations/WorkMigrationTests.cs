using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The <c>work</c> migration module (ISK-0009 T010, research R5 and R10, data model "isk_work.items" and
/// "isk_work.job_runs"): it is <c>work</c> 1 to 1 depending on <c>directory</c>; it applies and reverses; each constraint
/// rejects an out-of-range row; and with <c>coordination</c> the reverse order is <c>work</c>, <c>directory</c>,
/// <c>coordination</c>, so reversing two steps leaves <c>coordination</c> applied (persistence contract Migrations rule 12).
/// </summary>
public class WorkMigrationTests(PostgreSqlFixture fixture)
{
    private const string Valid =
        "'operator', gen_random_uuid(), 'config', 'config.publish', 1, '\\x00'::bytea, 'key', 0, 'pending', clock_timestamp()";

    private const string Columns =
        "scope_key, item_id, module, kind, payload_version, payload, idempotency_key, bucket, state, due_at";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheModuleIsWorkOneToOneDependingOnDirectory()
    {
        var module = WorkMigrations.Module;
        var set = new MigrationSet([DirectoryMigrations.Module, module]);

        Assert.Equal(("work", "isk_work", new SchemaVersionRange(1, 1)), (module.Name, module.Schema, module.SupportedRange));
        Assert.Equal(["directory"], module.DependsOn);
        Assert.Equal(["directory/1", "work/1"], set.Migrations.Select(script => script.Identity.ToString()));
        Assert.True(set.Migrations[1].DataLosing);
    }

    [Fact]
    public void TheForwardOrderIsCoordinationDirectoryWorkAndTheReverseOrderIsItsOpposite()
    {
        var set = new MigrationSet([WorkMigrations.Module, CoordinationMigrations.Module, DirectoryMigrations.Module]);

        Assert.Equal(["coordination/1", "directory/1", "work/1"], set.Migrations.Select(script => script.Identity.ToString()));
        Assert.Equal(["work/1", "directory/1", "coordination/1"], set.Reversed.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public async Task ReversingTwoStepsRemovesWorkThenDirectoryAndLeavesCoordinationApplied()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(new MigrationSet([CoordinationMigrations.Module, DirectoryMigrations.Module, WorkMigrations.Module]));
        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        var reversed = await runner.ReverseAsync(new MigrationReverseRequest { Steps = 2, ConfirmDataLoss = true }, Token);

        Assert.Equal([new MigrationIdentity("work", 1), new MigrationIdentity("directory", 1)], reversed.Reversed);
        Assert.Equal("coordination", await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(module, ',') FROM isk_migrations.history"));
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_coordination.leases') IS NOT NULL AND to_regclass('isk_work.items') IS NULL AND to_regclass('isk_directory.scopes') IS NULL"));
    }

    [Fact]
    public async Task ItAppliesWithBothTablesScopeKeyedAndReversesWithConfirmation()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module]));

        await runner.ApplyAsync(new MigrationApplyRequest(), Token);
        foreach (var table in new[] { "items", "job_runs" })
        {
            Assert.True(await fixture.ScalarAsync<bool>(database.Name, $"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'isk_work.{table}'::regclass"));
            Assert.Equal(
                PostgreSqlMigrationRunner.ScopeKeyedPolicyExpression,
                await fixture.ScalarAsync<string>(database.Name, $"SELECT regexp_replace(pg_get_expr(polqual, polrelid), '\\s+', ' ', 'g') FROM pg_policy WHERE polrelid = 'isk_work.{table}'::regclass AND polcmd = '*'"));
        }

        Assert.Equal("scope_key,item_id", await fixture.ScalarAsync<string>(database.Name, PrimaryKey("items")));
        Assert.Equal("scope_key,job", await fixture.ScalarAsync<string>(database.Name, PrimaryKey("job_runs")));
        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_work' AND indexdef LIKE '%(scope_key, kind, idempotency_key)%' AND indexdef LIKE 'CREATE UNIQUE%'"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_work' AND indexname = 'items_due' AND indexdef LIKE '%(scope_key, bucket, due_at) WHERE (state = ''pending''::text)%'"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_work' AND indexname = 'items_finished' AND indexdef LIKE '%(scope_key, state, finished_at) WHERE (state <> ''pending''::text)%'"));

        var unconfirmed = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, unconfirmed.Code);
        Assert.Equal([new MigrationIdentity("work", 1)], (await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token)).Reversed);
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regnamespace('isk_work') IS NOT NULL AND to_regclass('isk_work.items') IS NULL AND to_regclass('isk_directory.scopes') IS NOT NULL"));
    }

    public static TheoryData<string, string> OutOfRange => new()
    {
        { "payload_version 0", Valid.Replace("'config.publish', 1,", "'config.publish', 0,", StringComparison.Ordinal) },
        { "payload_version 1001", Valid.Replace("'config.publish', 1,", "'config.publish', 1001,", StringComparison.Ordinal) },
        { "payload over 65536", Valid.Replace("'\\x00'::bytea", "decode(repeat('00', 65537), 'hex')", StringComparison.Ordinal) },
        { "empty key", Valid.Replace("'key'", "''", StringComparison.Ordinal) },
        { "key over 128", Valid.Replace("'key'", "repeat('k', 129)", StringComparison.Ordinal) },
        { "bucket -1", Valid.Replace("'key', 0,", "'key', -1,", StringComparison.Ordinal) },
        { "bucket 64", Valid.Replace("'key', 0,", "'key', 64,", StringComparison.Ordinal) },
        { "unknown state", Valid.Replace("'pending'", "'running'", StringComparison.Ordinal) },
    };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public async Task EachItemConstraintRejectsAnOutOfRangeRow(string reason, string values)
    {
        Assert.NotEmpty(reason);
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using (var accepted = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            Assert.Equal(1, await PostgreSqlUnitOfWork.From(accepted).Command(WorkTables.Items, $"INSERT INTO isk_work.items ({Columns}) VALUES ({Valid.Replace("'operator'", "$1", StringComparison.Ordinal)})").ExecuteAsync(Token));
        }

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, $"INSERT INTO isk_work.items ({Columns}) VALUES ({values.Replace("'operator'", "$1", StringComparison.Ordinal)})").ExecuteAsync(Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(PersistenceFailureClass.Unexpected, unit.Failure!.FailureClass);
    }

    public static TheoryData<string, string> OutOfRangeColumns => new()
    {
        { "last_failure_class", "UPDATE isk_work.items SET last_failure_class = 'crashed' WHERE {scope}" },
        { "trace_parent", "UPDATE isk_work.items SET trace_parent = repeat('0', 56) WHERE {scope}" },
        { "trace_state", "UPDATE isk_work.items SET trace_state = repeat('s', 513) WHERE {scope}" },
        { "attempt_count", "UPDATE isk_work.items SET attempt_count = -1 WHERE {scope}" },
    };

    [Theory]
    [MemberData(nameof(OutOfRangeColumns))]
    public async Task EachOptionalColumnConstraintRejectsAnOutOfRangeValue(string column, string update)
    {
        Assert.NotEmpty(column);
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var adapter = PostgreSqlUnitOfWork.From(unit);
        await adapter.Command(WorkTables.Items, $"INSERT INTO isk_work.items ({Columns}) VALUES ({Valid.Replace("'operator'", "$1", StringComparison.Ordinal)})").ExecuteAsync(Token);

        await Assert.ThrowsAsync<GatewayException>(async () => await adapter.Command(WorkTables.Items, update).ExecuteAsync(Token));
    }

    [Fact]
    public async Task AJobCursorOverFiveHundredTwelveCharactersIsRejected()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var adapter = PostgreSqlUnitOfWork.From(unit);
        Assert.Equal(1, await adapter.Command(WorkTables.JobRuns, "INSERT INTO isk_work.job_runs (scope_key, job, cursor) VALUES ($1, 'work.retention', repeat('c', 512))").ExecuteAsync(Token));

        await Assert.ThrowsAsync<GatewayException>(async () => await adapter.Command(WorkTables.JobRuns, "UPDATE isk_work.job_runs SET cursor = repeat('c', 513) WHERE {scope}").ExecuteAsync(Token));
    }

    private static string PrimaryKey(string table) =>
        "SELECT string_agg(a.attname, ',' ORDER BY k.ord) FROM pg_index i CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ord) " +
        $"JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum WHERE i.indrelid = 'isk_work.{table}'::regclass AND i.indisprimary";
}

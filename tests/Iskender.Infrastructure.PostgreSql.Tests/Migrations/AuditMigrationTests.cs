using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// The <c>audit</c> migration module (ISK-0010 T007, research R12, data model "isk_audit.records"): it is <c>audit</c> 1 to
/// 1 depending on <c>work</c>; it applies and reverses; each <c>CHECK</c> rejects an out-of-list row; and the runtime login
/// may insert and read records but never update, delete, or truncate them (persistence contract Isolation rule 11, FR-029).
/// </summary>
public class AuditMigrationTests(PostgreSqlFixture fixture)
{
    private const string Columns = "audit_id, actor, target_principal, action, scope, result, reason, configuration_revision, correlation_id";

    private const string Valid = "'audit_1', 'anonymous', NULL, 'bootstrap.redeemed', 'operator', 'failed', 'consumed', 'rev-1', 'req_1'";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MigrationSet Set => new([DirectoryMigrations.Module, WorkMigrations.Module, AuditMigrations.Module]);

    [Fact]
    public void TheModuleIsAuditOneToOneDependingOnWork()
    {
        var module = AuditMigrations.Module;

        Assert.Equal(("audit", "isk_audit", new SchemaVersionRange(1, 1)), (module.Name, module.Schema, module.SupportedRange));
        Assert.Equal(["work"], module.DependsOn);
        Assert.Equal(["directory/1", "work/1", "audit/1"], Set.Migrations.Select(script => script.Identity.ToString()));
        Assert.True(Set.Migrations[2].DataLosing);
    }

    [Fact]
    public async Task ItAppliesAsAnOperatorTableAndReversesWithConfirmation()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var runner = store.Runner(Set);

        await runner.ApplyAsync(new MigrationApplyRequest(), Token);

        Assert.False(await fixture.ScalarAsync<bool>(database.Name, "SELECT relrowsecurity FROM pg_class WHERE oid = 'isk_audit.records'::regclass"));
        Assert.Equal(
            "audit_id,recorded_at,actor,target_principal,action,scope,result,reason,configuration_revision,correlation_id",
            await fixture.ScalarAsync<string>(database.Name, "SELECT string_agg(attname, ',' ORDER BY attnum) FROM pg_attribute WHERE attrelid = 'isk_audit.records'::regclass AND attnum > 0 AND NOT attisdropped"));
        Assert.Equal("statement_timestamp()", await fixture.ScalarAsync<string>(database.Name, "SELECT pg_get_expr(adbin, adrelid) FROM pg_attrdef d JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum WHERE d.adrelid = 'isk_audit.records'::regclass AND a.attname = 'recorded_at'"));
        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'isk_audit' AND indexname = 'records_recorded_at' AND indexdef LIKE '%(recorded_at)%'"));

        var unconfirmed = await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest(), Token));
        Assert.Same(ErrorCodes.PersistenceReverseRequiresConfirmation, unconfirmed.Code);
        Assert.Equal([new MigrationIdentity("audit", 1)], (await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = true }, Token)).Reversed);
        Assert.True(await fixture.ScalarAsync<bool>(database.Name, "SELECT to_regclass('isk_audit.records') IS NULL AND to_regclass('isk_work.items') IS NOT NULL"));
    }

    public static TheoryData<string, string> OutOfList => new()
    {
        { "unknown action", Valid.Replace("'bootstrap.redeemed'", "'bootstrap.deleted'", StringComparison.Ordinal) },
        { "tenant scope", Valid.Replace("'operator'", "'organization'", StringComparison.Ordinal) },
        { "unknown result", Valid.Replace("'failed'", "'partial'", StringComparison.Ordinal) },
        { "unknown reason", Valid.Replace("'consumed'", "'malformed'", StringComparison.Ordinal) },
        { "null actor", Valid.Replace("'anonymous'", "NULL", StringComparison.Ordinal) },
        { "null revision", Valid.Replace("'rev-1'", "NULL", StringComparison.Ordinal) },
        { "null correlation", Valid.Replace("'req_1'", "NULL", StringComparison.Ordinal) },
    };

    [Theory]
    [MemberData(nameof(OutOfList))]
    public async Task EachConstraintRejectsAnOutOfListRow(string reason, string values)
    {
        Assert.NotEmpty(reason);
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(Set).ApplyAsync(new MigrationApplyRequest(), Token);

        await using (var accepted = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            Assert.Equal(1, await PostgreSqlUnitOfWork.From(accepted).Command(AuditTables.Records, $"INSERT INTO isk_audit.records ({Columns}) VALUES ({Valid})").ExecuteAsync(Token));
        }

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await PostgreSqlUnitOfWork.From(unit).Command(AuditTables.Records, $"INSERT INTO isk_audit.records ({Columns}) VALUES ({values})").ExecuteAsync(Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
    }

    [Fact]
    public async Task TheRuntimeLoginCanInsertAndReadButNeverUpdateDeleteOrTruncate()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(Set).ApplyAsync(new MigrationApplyRequest(), Token);

        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(Token);
        await using (var insert = new NpgsqlCommand($"INSERT INTO isk_audit.records ({Columns}) VALUES ({Valid})", connection))
        {
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(Token));
        }

        await using (var select = new NpgsqlCommand("SELECT count(*) FROM isk_audit.records", connection))
        {
            Assert.Equal(1L, (long)(await select.ExecuteScalarAsync(Token))!);
        }

        foreach (var statement in new[]
        {
            "UPDATE isk_audit.records SET reason = 'none'",
            "DELETE FROM isk_audit.records",
            "TRUNCATE isk_audit.records",
        })
        {
            await using var command = new NpgsqlCommand(statement, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Token));
            Assert.True(failure.SqlState is PostgresErrorCodes.InsufficientPrivilege, $"{statement}: {failure.SqlState}");
        }

        Assert.Equal(1L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_audit.records WHERE reason = 'consumed'"));
    }
}

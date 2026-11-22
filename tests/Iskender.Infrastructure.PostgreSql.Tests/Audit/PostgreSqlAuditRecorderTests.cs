using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Domain.Telemetry;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Tests.Audit;

/// <summary>
/// The PostgreSQL audit recorder (ISK-0010 T008, research R12): a record commits or rolls back with its unit, holds every
/// column, an invalid record fails before any statement, and a tenant-scope unit is refused (FR-026, FR-027).
/// </summary>
public class PostgreSqlAuditRecorderTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AuditRecord Record(AuditResult result = AuditResult.Failed, AuditReason reason = AuditReason.WrongSecret, ResourceId? target = null) =>
        new(AuditActor.Anonymous, target, AuditAction.BootstrapRedeemed, result, reason, new ConfigurationRevision("rev-1"), RequestCorrelationId.New());

    private async Task<(ScratchDatabase Database, TestStore Store)> MigratedAsync()
    {
        fixture.RequirePostgreSql();
        var database = await ScratchDatabase.CreateAsync(fixture);
        var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module, AuditMigrations.Module])).ApplyAsync(new MigrationApplyRequest(), Token);
        return (database, store);
    }

    [Fact]
    public async Task ARecordCommittedWithItsUnitHoldsEveryColumn()
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;
        var principal = ResourceId.New(ResourceTypes.Principal);
        var record = new AuditRecord(
            AuditActor.ForPrincipal(principal), principal, AuditAction.EnrollmentConsumed, AuditResult.Succeeded, AuditReason.None,
            new ConfigurationRevision("rev-7"), RequestCorrelationId.New());

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            await new PostgreSqlAuditRecorder().AppendAsync(unit, record, Token);
            await unit.CommitAsync(Token);
        }

        Assert.Equal(
            $"{principal.Value}|{principal.Value}|enrollment.consumed|operator|succeeded|none|rev-7|{record.CorrelationId.Value}|audit|true",
            await fixture.ScalarAsync<string>(database.Name,
                "SELECT actor || '|' || target_principal || '|' || action || '|' || scope || '|' || result || '|' || reason || '|' || configuration_revision || '|' || correlation_id || '|' || split_part(audit_id, '_', 1) || '|' || (recorded_at <= statement_timestamp())::text FROM isk_audit.records"));
    }

    [Fact]
    public async Task ARecordInARolledBackUnitDoesNotExist()
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;

        await using (var unit = await store.OpenAsync(UnitOfWorkScope.Operator))
        {
            await new PostgreSqlAuditRecorder().AppendAsync(unit, Record(), Token);
        }

        Assert.Equal(0L, await fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_audit.records"));
    }

    public static TheoryData<string> Invalid => ["none on a failure", "a reason on a success", "a null actor", "a tenant target", "null revision", "null correlation"];

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task AnInvalidRecordFailsBeforeAnyStatement(string which)
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;
        var record = which switch
        {
            "none on a failure" => Record(AuditResult.Failed, AuditReason.None),
            "a reason on a success" => Record(AuditResult.Succeeded, AuditReason.Consumed),
            "a null actor" => Record() with { Actor = null! },
            "a tenant target" => Record(target: ResourceId.New(ResourceTypes.Project)),
            "null revision" => Record() with { ConfigurationRevision = null! },
            _ => Record() with { CorrelationId = null! },
        };

        await using var unit = await store.OpenAsync(UnitOfWorkScope.Operator);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await new PostgreSqlAuditRecorder().AppendAsync(unit, record, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(0, PostgreSqlUnitOfWork.From(unit).StatementsSent);
        Assert.Equal(UnitOfWorkState.Open, unit.State);
    }

    [Fact]
    public async Task ATenantScopeUnitIsRefused()
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await new PostgreSqlAuditRecorder().AppendAsync(unit, Record(), Token));

        Assert.Same(ErrorCodes.PersistenceScopeNotPermitted, failure.Code);
        Assert.Equal(0, PostgreSqlUnitOfWork.From(unit).StatementsSent);
    }
}

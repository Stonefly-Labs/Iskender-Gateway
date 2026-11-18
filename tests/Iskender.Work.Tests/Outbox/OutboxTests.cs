using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Outbox;

/// <summary>
/// The outbox (ISK-0009 T014, <c>work</c> contract The outbox and Work items, US1.1, US1.4, FR-002, FR-003, FR-009). Each
/// test uses its own idempotency keys, so the tests of the class share one database without sharing items.
/// </summary>
public class OutboxTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static readonly WorkKind Touch = new("applies", "touch");

    private static readonly PostgreSqlWorkOutbox Outbox = new([new WorkKindDeclaration(Touch, 2)], logger: null);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACommittedEnqueueLeavesAPendingDueItemAndTheScopesDirectoryRow()
    {
        work.RequirePostgreSql();

        await using var unit = await work.OpenAsync(WorkFixture.OrganizationA);
        var result = await Outbox.EnqueueAsync(unit, Request(WorkFixture.OrganizationA, "commit"), Token);
        await unit.CommitAsync(Token);

        Assert.True(result.Created);
        Assert.Equal("pending|0|true", await work.ScalarAsync<string>($"SELECT state || '|' || attempt_count || '|' || (due_at <= clock_timestamp()) FROM isk_work.items WHERE item_id = '{result.ItemId}'"));
        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND listed_scope = 'org:{Owners.OrgA}' AND due_at IS NOT NULL"));
    }

    [Fact]
    public async Task ARolledBackEnqueueLeavesNeitherTheChangeNorTheItemNorADirectoryRow()
    {
        work.RequirePostgreSql();
        var item = Guid.Empty;

        await using (var unit = await work.OpenAsync(WorkFixture.OrganizationB))
        {
            await Apply(unit, Guid.NewGuid());
            item = (await Outbox.EnqueueAsync(unit, Request(WorkFixture.OrganizationB, "rollback"), Token)).ItemId;
        }

        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE item_id = '{item}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_applies.applies WHERE scope_key = 'org:{Owners.OrgB}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE listed_scope = 'org:{Owners.OrgB}'"));
    }

    public static TheoryData<string> Refusals() => ["scope_mismatch", "kind_undeclared", "version_above_highest", "payload_too_large"];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ARefusedEnqueueFaultsTheUnitBeforeAnyWriteAndCommitsNothing(string refusal)
    {
        work.RequirePostgreSql();
        var key = "refused-" + refusal.Replace('_', '-');
        var (request, code) = refusal switch
        {
            "scope_mismatch" => (Request(WorkFixture.OrganizationB, key), ErrorCodes.WorkScopeMismatch),
            "kind_undeclared" => (new WorkItemRequest(Owner(WorkFixture.ProjectA1), new WorkKind("applies", "other"), 1, new byte[1], key), ErrorCodes.WorkKindUndeclared),
            "version_above_highest" => (new WorkItemRequest(Owner(WorkFixture.ProjectA1), Touch, 3, new byte[1], key), ErrorCodes.WorkKindUndeclared),
            _ => (new WorkItemRequest(Owner(WorkFixture.ProjectA1), Touch, 1, new byte[PostgreSqlWorkOutbox.MaximumPayloadBytes + 1], key), ErrorCodes.WorkPayloadTooLarge),
        };

        await using var unit = await work.OpenAsync(WorkFixture.ProjectA1);
        var adapter = PostgreSqlUnitOfWork.From(unit);
        var applied = Guid.NewGuid();
        await Apply(unit, applied);
        var sent = adapter.StatementsSent;

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Outbox.EnqueueAsync(unit, request, Token));

        Assert.Same(code, failure.Code);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Same(code, unit.Failure!.Code);
        Assert.Equal(sent, adapter.StatementsSent);
        await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE idempotency_key = '{key}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_applies.applies WHERE item_id = '{applied}'"));
    }

    // The bound is inclusive: a payload of exactly 65,536 bytes is enqueued, committed, and stored whole (ISK-0009 T075,
    // FR-002, Work items rule 2).
    [Fact]
    public async Task APayloadOfExactly65536BytesIsEnqueuedAndCommitted()
    {
        work.RequirePostgreSql();
        var payload = new byte[65_536];
        Random.Shared.NextBytes(payload);

        var result = await EnqueueCommittedAsync(WorkFixture.ProjectA1, new WorkItemRequest(Owner(WorkFixture.ProjectA1), Touch, 1, payload, "payload-at-bound"));

        Assert.True(result.Created);
        Assert.Equal(PostgreSqlWorkOutbox.MaximumPayloadBytes, payload.Length);
        Assert.Equal("pending|65536", await work.ScalarAsync<string>($"SELECT state || '|' || octet_length(payload) FROM isk_work.items WHERE item_id = '{result.ItemId}'"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(payload)), await work.ScalarAsync<string>($"SELECT encode(sha256(payload), 'hex') FROM isk_work.items WHERE item_id = '{result.ItemId}'"));
    }

    public static TheoryData<bool, bool, bool, bool, string> CombinedRefusals() => new()
    {
        // Not open, another scope, an undeclared kind, an oversized payload, and the first code in contract order.
        { false, true, true, true, ErrorCodes.WorkScopeMismatch.Id },
        { false, true, false, true, ErrorCodes.WorkScopeMismatch.Id },
        { false, false, true, true, ErrorCodes.WorkKindUndeclared.Id },
        { true, true, true, true, ErrorCodes.WorkUnitNotOpen.Id },
    };

    // With several conditions broken at once, the enqueue fails with the first in the contract's order, before any write
    // (ISK-0009 T076, The outbox rule 2).
    [Theory]
    [MemberData(nameof(CombinedRefusals))]
    public async Task SeveralBrokenConditionsFailWithTheFirstInContractOrder(bool notOpen, bool otherScope, bool undeclared, bool oversized, string code)
    {
        work.RequirePostgreSql();
        var key = $"combined-{notOpen}-{otherScope}-{undeclared}-{oversized}".ToLowerInvariant();
        var request = new WorkItemRequest(
            Owner(otherScope ? WorkFixture.OrganizationB : WorkFixture.ProjectA1),
            undeclared ? new WorkKind("applies", "other") : Touch,
            1,
            new byte[oversized ? PostgreSqlWorkOutbox.MaximumPayloadBytes + 1 : 1],
            key);
        await using var unit = await work.OpenAsync(WorkFixture.ProjectA1);
        if (notOpen)
        {
            await unit.CommitAsync(Token);
        }

        var sent = PostgreSqlUnitOfWork.From(unit).StatementsSent;
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Outbox.EnqueueAsync(unit, request, Token));

        Assert.Equal(code, failure.Code.Id);
        Assert.Equal(sent, PostgreSqlUnitOfWork.From(unit).StatementsSent);
        if (!notOpen)
        {
            Assert.Equal(UnitOfWorkState.Faulted, unit.State);
            Assert.Equal(code, unit.Failure!.Code.Id);
        }

        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE idempotency_key = '{key}'"));
    }

    [Fact]
    public async Task AnEnqueueInAUnitThatIsNotOpenFailsWithUnitNotOpen()
    {
        work.RequirePostgreSql();

        await using var unit = await work.OpenAsync(WorkFixture.ProjectA1);
        await unit.CommitAsync(Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Outbox.EnqueueAsync(unit, Request(WorkFixture.ProjectA1, "not-open"), Token));

        Assert.Same(ErrorCodes.WorkUnitNotOpen, failure.Code);
        Assert.Equal(0L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_work.items WHERE idempotency_key = 'not-open'"));
    }

    [Fact]
    public async Task AnOperatorScopeItemIsWrittenAndReadOnlyInAnOperatorUnit()
    {
        work.RequirePostgreSql();

        await using (var unit = await work.OpenAsync(WorkFixture.Operator))
        {
            Assert.True((await Outbox.EnqueueAsync(unit, Request(WorkFixture.Operator, "operator"), Token)).Created);
            await unit.CommitAsync(Token);
        }

        Assert.Equal("operator", await work.ScalarAsync<string>("SELECT scope_key FROM isk_work.items WHERE idempotency_key = 'operator'"));
        Assert.Equal(1L, await CountVisibleAsync(WorkFixture.Operator, "operator"));
        Assert.Equal(0L, await CountVisibleAsync(WorkFixture.OrganizationA, "operator"));
        Assert.Equal(1L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND listed_scope = 'operator'"));
    }

    [Fact]
    public async Task ASecondEnqueueWithTheSameKeyReturnsTheRetainedItemAndChangesNothing()
    {
        work.RequirePostgreSql();
        var first = await EnqueueCommittedAsync(WorkFixture.OrganizationA, new WorkItemRequest(Owner(WorkFixture.OrganizationA), Touch, 1, new byte[] { 1 }, "same-key"));

        var second = await EnqueueCommittedAsync(WorkFixture.OrganizationA, new WorkItemRequest(Owner(WorkFixture.OrganizationA), Touch, 2, new byte[] { 2, 2 }, "same-key", TimeSpan.FromHours(1)));

        Assert.Equal((first.ItemId, true), (first.ItemId, first.Created));
        Assert.Equal((first.ItemId, false), (second.ItemId, second.Created));
        Assert.Equal("1|\\x01|true", await work.ScalarAsync<string>("SELECT payload_version || '|' || payload::text || '|' || (due_at <= clock_timestamp()) FROM isk_work.items WHERE idempotency_key = 'same-key'"));
    }

    [Fact]
    public async Task TheSameKeyInAnotherScopeOrKindIsAnotherItem()
    {
        work.RequirePostgreSql();

        var a = await EnqueueCommittedAsync(WorkFixture.OrganizationA, Request(WorkFixture.OrganizationA, "per-scope"));
        var b = await EnqueueCommittedAsync(WorkFixture.OrganizationB, Request(WorkFixture.OrganizationB, "per-scope"));

        Assert.True(a.Created && b.Created);
        Assert.NotEqual(a.ItemId, b.ItemId);
    }

    [Fact]
    public async Task TwoConcurrentProducersWithOneKeyLeaveOneItem()
    {
        work.RequirePostgreSql();

        await using var first = await work.OpenAsync(WorkFixture.ProjectA1);
        var created = await Outbox.EnqueueAsync(first, Request(WorkFixture.ProjectA1, "race"), Token);
        await using var second = await work.OpenAsync(WorkFixture.ProjectA1);
        var racing = Outbox.EnqueueAsync(second, Request(WorkFixture.ProjectA1, "race"), Token).AsTask();
        await WaitForLockWaitAsync();

        await first.CommitAsync(Token);
        var existing = await racing;
        await second.CommitAsync(Token);

        Assert.True(created.Created);
        Assert.Equal((created.ItemId, false), (existing.ItemId, existing.Created));
        Assert.Equal(1L, await work.ScalarAsync<long>("SELECT count(*) FROM isk_work.items WHERE idempotency_key = 'race'"));
    }

    [Fact]
    public async Task TheDelayIsMeasuredOnTheDatabaseClock()
    {
        work.RequirePostgreSql();

        var result = await EnqueueCommittedAsync(WorkFixture.OrganizationB, new WorkItemRequest(Owner(WorkFixture.OrganizationB), Touch, 1, new byte[1], "delayed", TimeSpan.FromMinutes(10)));

        // due_at and enqueued_at are both clock_timestamp() calls in the same insert, so they differ by the delay, give or
        // take the microseconds between the two calls.
        var gap = await work.ScalarAsync<double>($"SELECT extract(epoch FROM due_at - enqueued_at)::float8 FROM isk_work.items WHERE item_id = '{result.ItemId}'");
        Assert.InRange(gap, 599.99d, 600.01d);
        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'work' AND listed_scope = 'org:{Owners.OrgB}' AND due_at > clock_timestamp() + interval '9 minutes'"));
    }

    [Fact]
    public async Task TheStoredBucketIsTheContractsFormula()
    {
        work.RequirePostgreSql();

        var result = await EnqueueCommittedAsync(WorkFixture.Operator, Request(WorkFixture.Operator, "bucket"));

        Assert.Equal(Expected(result.ItemId), await work.ScalarAsync<short>($"SELECT bucket FROM isk_work.items WHERE item_id = '{result.ItemId}'"));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0192f5a1-7c3e-7b2a-9d4e-6f8a1b2c3d4e")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void TheBucketMatchesTheFormulaForAFixedId(string id)
    {
        var item = Guid.Parse(id);

        Assert.Equal(Expected(item), PostgreSqlWorkOutbox.Bucket(item));
        Assert.InRange(PostgreSqlWorkOutbox.Bucket(item), 0, 63);
    }

    // The formula, written out separately: SHA-256 over "iskender.work.bucket.v1", 0x00, and the lowercase ID text.
    private static short Expected(Guid item) =>
        (short)(SHA256.HashData([.. Encoding.UTF8.GetBytes("iskender.work.bucket.v1"), 0x00, .. Encoding.UTF8.GetBytes(item.ToString().ToLowerInvariant())])[0] % 64);

    private static OwnerScope Owner(UnitOfWorkScope scope) => scope.Owner;

    private static WorkItemRequest Request(UnitOfWorkScope scope, string key) => new(scope.Owner, Touch, 1, new byte[] { 7 }, key);

    private static async Task Apply(IUnitOfWork unit, Guid item) =>
        await PostgreSqlUnitOfWork.From(unit).Command(AppliesModule.Applies, "INSERT INTO isk_applies.applies (scope_key, item_id, attempt) VALUES ($1, $2, 1)").With(item).ExecuteAsync(Token);

    private async Task<WorkEnqueueResult> EnqueueCommittedAsync(UnitOfWorkScope scope, WorkItemRequest request)
    {
        await using var unit = await work.OpenAsync(scope);
        var result = await Outbox.EnqueueAsync(unit, request, Token);
        await unit.CommitAsync(Token);
        return result;
    }

    private async Task<long> CountVisibleAsync(UnitOfWorkScope scope, string key)
    {
        await using var unit = await work.OpenAsync(scope);
        return await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, "SELECT count(*) FROM isk_work.items WHERE {scope} AND idempotency_key = $2").With(key).ScalarAsync<long>(Token);
    }

    // Waits, without a fixed sleep, until a session of this database is waiting on a row lock.
    private async Task WaitForLockWaitAsync()
    {
        while (await work.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE datname = '{work.DatabaseName}' AND wait_event_type = 'Lock'") == 0)
        {
            Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Isolation;

/// <summary>
/// Handler scope (ISK-0009 T036, US4.1, US4.2, FR-009, SC-005): a handler runs in a unit of its item's scope and cannot
/// change another scope's rows, a producer cannot enqueue for another scope, and an operator unit handles operator items
/// and reads no tenant item. Each assertion runs with the <c>scope_key</c> predicate present and removed, so row-level
/// security alone holds.
/// </summary>
public class HandlerScopeTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    public static TheoryData<bool> Predicate() => [true, false];

    [Theory]
    [MemberData(nameof(Predicate))]
    public async Task AnItemIsHandledInItsScopesUnitAndItsWriteToAnotherScopesRowsChangesNothing(bool predicate)
    {
        work.RequirePostgreSql();
        var a = WorkFixture.FreshOrganization();
        var b = WorkFixture.FreshOrganization();
        var foreign = Guid.NewGuid();
        await using (var unit = await work.OpenAsync(b))
        {
            await PostgreSqlUnitOfWork.From(unit).Command(AppliesModule.Applies, "INSERT INTO isk_applies.applies (scope_key, item_id, attempt) VALUES ($1, $2, 1)")
                .With(foreign).ExecuteAsync(TestContext.Current.CancellationToken);
            await unit.CommitAsync(TestContext.Current.CancellationToken);
        }

        var item = await work.EnqueueAsync(a, ReachingHandler.Kind, "reach-" + predicate);
        ReachingHandler.Target = foreign;
        ReachingHandler.WithPredicate = predicate;
        await using (var dispatcher = new TestDispatcher(work, [ReachingHandler.Registration()], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal(0, ReachingHandler.Changed[item]);
        Assert.Equal(1, await work.ScalarAsync<int>($"SELECT attempt FROM isk_applies.applies WHERE item_id = '{foreign}'"));
        Assert.Equal(
            await work.ScalarAsync<string>($"SELECT scope_key FROM isk_work.items WHERE item_id = '{item}'"),
            await work.ScalarAsync<string>($"SELECT scope_key FROM isk_applies.applies WHERE item_id = '{item}'"));
        Assert.Equal(a.Owner, Assert.Single(ApplyRecordingHandler.Deliveries, delivery => delivery.ItemId == item).Owner);
    }

    [Fact]
    public async Task AProducerInOneScopeCannotEnqueueForAnotherBeforeAnyWrite()
    {
        work.RequirePostgreSql();
        var a = WorkFixture.FreshOrganization();
        var b = WorkFixture.FreshOrganization();
        var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(TestKinds.Record, 1)], logger: null);
        var items = await work.ScalarAsync<long>("SELECT count(*) FROM isk_work.items");

        await using (var unit = await work.OpenAsync(a))
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
                await outbox.EnqueueAsync(unit, new WorkItemRequest(b.Owner, TestKinds.Record, 1, new byte[] { 1 }, "mismatch"), TestContext.Current.CancellationToken));
            Assert.Same(ErrorCodes.WorkScopeMismatch, failure.Code);
        }

        Assert.Equal(items, await work.ScalarAsync<long>("SELECT count(*) FROM isk_work.items"));
    }

    [Theory]
    [MemberData(nameof(Predicate))]
    public async Task AnOperatorItemIsHandledInAnOperatorUnitWhichReadsNoTenantItem(bool predicate)
    {
        work.RequirePostgreSql();
        var tenant = await work.EnqueueAsync(WorkFixture.FreshOrganization(), TestKinds.Record, "tenant-" + predicate);
        var item = await work.EnqueueAsync(WorkFixture.Operator, ReachingHandler.Kind, "operator-" + predicate);
        ReachingHandler.Target = Guid.NewGuid();
        ReachingHandler.WithPredicate = predicate;
        await using (var dispatcher = new TestDispatcher(work, [ReachingHandler.Registration()], WorkTestSettings.Fast))
        {
            await dispatcher.PassAsync();
        }

        Assert.Equal("completed|1", await work.ItemAsync(item));
        Assert.Equal("operator", await work.ScalarAsync<string>($"SELECT scope_key FROM isk_applies.applies WHERE item_id = '{item}'"));

        await using var unit = await work.OpenAsync(WorkFixture.Operator, new UnitOfWorkOptions("work", "test.read"));
        var visible = await PostgreSqlUnitOfWork.From(unit)
            .Command(WorkTables.Items, "SELECT item_id FROM isk_work.items WHERE " + (predicate ? "{scope}" : "$1::text IS NOT NULL"))
            .QueryAsync(reader => reader.GetGuid(0), TestContext.Current.CancellationToken);
        Assert.Contains(item, visible);
        Assert.DoesNotContain(tenant, visible);
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE scope_key <> 'operator' AND item_id IN ('{string.Join("','", visible)}')"));
    }

    // Records its apply, then tries to change Target's applies row of another scope, counting the rows it changed.
    private sealed class ReachingHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "reaching");

        public static Guid Target { get; set; }

        public static bool WithPredicate { get; set; }

        public static Dictionary<Guid, int> Changed { get; } = [];

        public static WorkHandlerRegistration Registration() => new(Kind, 1, 1, null, null, false, typeof(ReachingHandler));

        public async ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
        {
            ApplyRecordingHandler.Deliveries.Enqueue(delivery);
            await ApplyRecordingHandler.RecordAsync(delivery, unit, cancellationToken);
            Changed[delivery.ItemId] = await PostgreSqlUnitOfWork.From(unit)
                .Command(AppliesModule.Applies, "UPDATE isk_applies.applies SET attempt = 99 WHERE item_id = $2 AND " + (WithPredicate ? "{scope}" : "$1::text IS NOT NULL"))
                .With(Target).ExecuteAsync(cancellationToken);
        }
    }
}

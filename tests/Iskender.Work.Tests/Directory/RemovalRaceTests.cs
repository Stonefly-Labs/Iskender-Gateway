using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Directory;

/// <summary>
/// The directory removal race (ISK-0009 T038, SC-005, plan Risks, research R3): settles run repeatedly against
/// concurrent producers enqueuing in the same scope, every statement of both held for a random pause, at each producer
/// isolation level. After every round each committed item has its <c>work</c> directory row, and a producer that loses a
/// race fails with <c>persistence.concurrency_conflict</c> and commits nothing, never leaving an item without its row. The
/// settles are the dispatcher's own, <see cref="WorkDispatcher.SettleAsync(IUnitOfWorkFactory, UnitOfWorkScope, CancellationToken)"/>
/// (T083).
/// The <c>coordination</c> module's race is T040's.
/// </summary>
public class RemovalRaceTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const int Rounds = 25;
    private const int Producers = 3;
    private const int Settles = 3;

    // Records each directory row a settle deletes. The class's database is its own, so nothing else deletes one.
    private const string DeletesSql = """
        CREATE TABLE IF NOT EXISTS public.directory_deletes (listed_scope text NOT NULL);
        TRUNCATE public.directory_deletes;
        CREATE OR REPLACE FUNCTION public.record_directory_delete() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
        BEGIN
          INSERT INTO public.directory_deletes VALUES (OLD.listed_scope);
          RETURN NULL;
        END $$;
        DROP TRIGGER IF EXISTS record_directory_delete ON isk_directory.scopes;
        CREATE TRIGGER record_directory_delete AFTER DELETE ON isk_directory.scopes FOR EACH ROW EXECUTE FUNCTION public.record_directory_delete();
        """;

    public static TheoryData<UnitOfWorkIsolation> Isolations() =>
        [UnitOfWorkIsolation.ReadCommitted, UnitOfWorkIsolation.RepeatableRead, UnitOfWorkIsolation.Serializable];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Isolations))]
    public async Task EveryCommittedItemKeepsItsDirectoryRowAndALostRaceCommitsNothing(UnitOfWorkIsolation isolation)
    {
        work.RequirePostgreSql();
        await using var proxy = new OutageProxy(work.Postgres.Host, work.Postgres.Port) { Pause = () => TimeSpan.FromMilliseconds(Random.Shared.Next(0, 6)) };
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = "127.0.0.1", ["port"] = proxy.Port };
        await using var sources = new PostgreSqlDataSources(TestSettings.For(work.Database, overrides), TestCredentialProvider.For(work.Postgres));
        var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, work.Logger);
        var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(TestKinds.Record, 1)], logger: null);
        await work.ExecuteAsync(DeletesSql);

        var conflicts = 0;
        for (var round = 0; round < Rounds; round++)
        {
            // Each round starts from a scope with a row and no item, so every settle that wins deletes the row.
            var scope = WorkFixture.FreshOrganization();
            await using (var unit = await work.OpenAsync(scope, new UnitOfWorkOptions("work", "test.register")))
            {
                await ScopeDirectory.RegisterAsync(unit, "work", DateTime.UtcNow, Token);
                await unit.CommitAsync(Token);
            }

            var producers = Enumerable.Range(0, Producers).Select(index => ProduceAsync(units, outbox, scope, isolation, $"race-{isolation}-{round}-{index}")).ToList();
            var settles = Enumerable.Range(0, Settles).Select(_ => WorkDispatcher.SettleAsync(units, scope, Token)).ToList();
            await Task.WhenAll(settles);
            var outcomes = await Task.WhenAll(producers);
            conflicts += outcomes.Count(outcome => outcome.Failure is not null);

            foreach (var (key, failure) in outcomes)
            {
                if (failure is not null)
                {
                    Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, failure.Code);
                    Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_work.items WHERE idempotency_key = '{key}'"));
                }
            }

            Assert.Equal(0L, await work.ScalarAsync<long>(
                "SELECT count(*) FROM isk_work.items i WHERE i.idempotency_key LIKE 'race-%' "
                + "AND NOT EXISTS (SELECT 1 FROM isk_directory.scopes d WHERE d.listed_scope = i.scope_key AND d.module = 'work')"));
        }

        // The races happened: settles deleted rows the producers needed, and stricter producers lost to them. At Read
        // Committed, register's rounds take a lost race in its stride. The removals are the trigger's record of deleted rows.
        var removals = await work.ScalarAsync<long>("SELECT count(*) FROM public.directory_deletes");
        Assert.True(removals > 0, "No settle removed a row, so no race was run.");
        if (isolation != UnitOfWorkIsolation.ReadCommitted)
        {
            Assert.True(conflicts > 0, "No producer lost a race to a settle.");
        }
    }

    // Enqueues one item at isolation in its own unit and commits, returning the failure it lost a race with, if any.
    private static async Task<(string Key, GatewayException? Failure)> ProduceAsync(
        PostgreSqlUnitOfWorkFactory units, PostgreSqlWorkOutbox outbox, UnitOfWorkScope scope, UnitOfWorkIsolation isolation, string key)
    {
        try
        {
            await using var unit = await units.OpenAsync(scope, new UnitOfWorkOptions("applies", "test.produce") { Isolation = isolation }, Token);
            await outbox.EnqueueAsync(unit, new WorkItemRequest(scope.Owner, TestKinds.Record, 1, new byte[] { 1 }, key), Token);
            await unit.CommitAsync(Token);
            return (key, null);
        }
        catch (GatewayException failure)
        {
            return (key, failure);
        }
    }
}

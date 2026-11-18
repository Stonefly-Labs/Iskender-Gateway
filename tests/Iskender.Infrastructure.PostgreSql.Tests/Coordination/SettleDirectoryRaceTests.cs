using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceScopes = Iskender.Coordination.Conformance.ConformanceScopes;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The directory removal race for the <c>coordination</c> module (ISK-0009 T040, SC-005, research R3): settles run
/// repeatedly against concurrent PostgreSQL coordinator writes that insert new atomic-operation and lease rows in the same
/// scope, every statement of both held for a random pause, at each producer isolation level. After every round each
/// expiring row of the scope has its <c>coordination</c> directory row, and a write that loses a race fails its unit with
/// <c>persistence.concurrency_conflict</c> and commits nothing.
/// </summary>
/// <remarks>
/// The coordinator opens every unit at Read Committed, so a factory that raises the isolation level stands in for a
/// stricter producer. Its lost race reaches the caller as <c>core.internal</c>, the coordination contract's mapping of any
/// other persistence failure, so the conflict is read from the unit the write ran in.
/// </remarks>
public class SettleDirectoryRaceTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private const int Rounds = 20;
    private const int Producers = 4;
    private const int Settles = 3;

    // Records each directory row a settle deletes. The class's database is its own, so nothing else deletes one.
    private const string DeletesSql = """
        CREATE TABLE IF NOT EXISTS public.directory_deletes (listed_scope text NOT NULL);
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
    public async Task EveryExpiringRowKeepsItsDirectoryRowAndALostRaceCommitsNothing(UnitOfWorkIsolation isolation)
    {
        coordinator.Require();
        await using var proxy = new OutageProxy(coordinator.Fixture.Host, coordinator.Fixture.Port) { Pause = () => TimeSpan.FromMilliseconds(Random.Shared.Next(0, 6)) };
        await using var store = TestStore.For(coordinator.Database, new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = "127.0.0.1", ["port"] = proxy.Port });
        var maintenance = new CoordinationMaintenance(store.Factory, coordinator.CoordinationSettings);
        await coordinator.Fixture.ExecuteAsync(coordinator.Database.Name, DeletesSql);

        var (removals, conflicts) = (0, 0);
        for (var round = 0; round < Rounds; round++)
        {
            // Each round starts from a scope with a row and no expiring row, so every settle that wins deletes the row.
            var scope = new ConformanceScopes().OrganizationA;
            await using (var unit = await coordinator.Units(0).OpenAsync(scope.UnitScope, new UnitOfWorkOptions("coordination", "test.register"), Token))
            {
                await ScopeDirectory.RegisterAsync(unit, "coordination", DateTime.UtcNow, Token);
                await unit.CommitAsync(Token);
            }

            var producers = Enumerable.Range(0, Producers).Select(index => ProduceAsync(store.Factory, scope, isolation, index)).ToList();
            var settles = Enumerable.Range(0, Settles).Select(_ => maintenance.SettleDirectoryAsync(scope, Token).AsTask()).ToList();
            await Task.WhenAll(settles);
            var outcomes = await Task.WhenAll(producers);
            removals += (int)await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM public.directory_deletes WHERE listed_scope = '{ScopeKey(scope)}'");

            foreach (var (table, name, failure) in outcomes)
            {
                if (failure is not null)
                {
                    conflicts++;
                    Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, failure.Code);
                    Assert.Equal(0L, await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM isk_coordination.{table} WHERE scope_key = '{ScopeKey(scope)}' AND key_hash = '{CoordinationKey.Hash(scope.Owner, "settle_race", name)}'"));
                }
            }

            Assert.Equal(0L, await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name,
                $"SELECT count(*) FROM (SELECT scope_key FROM isk_coordination.coordination_values UNION ALL SELECT scope_key FROM isk_coordination.leases) r WHERE r.scope_key = '{ScopeKey(scope)}' "
                + "AND NOT EXISTS (SELECT 1 FROM isk_directory.scopes d WHERE d.listed_scope = r.scope_key AND d.module = 'coordination')"));
        }

        // The races happened: settles deleted rows the writes needed, and stricter writes lost to them.
        Assert.True(removals > 0, "No settle removed a row, so no race was run.");
        if (isolation != UnitOfWorkIsolation.ReadCommitted)
        {
            Assert.True(conflicts > 0, "No write lost a race to a settle.");
        }
    }

    // Inserts a new atomic value or lease at isolation, returning its table, its name, and the unit failure of a lost race.
    private async Task<(string Table, string Name, PersistenceFailure? Failure)> ProduceAsync(PostgreSqlUnitOfWorkFactory units, CoordinationScope scope, UnitOfWorkIsolation isolation, int index)
    {
        var isolating = new IsolatingUnits(units, isolation);
        var name = "key_" + index;
        var lease = index % 2 == 1;
        try
        {
            if (lease)
            {
                await new PostgreSqlLeaseCoordinator(isolating, coordinator.CoordinationSettings).AcquireAsync(scope, new CoordinationName("settle_race", name), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token);
            }
            else
            {
                await new PostgreSqlAtomicOperations(isolating, coordinator.CoordinationSettings).AddAsync(scope, new CoordinationName("settle_race", name), 1, TimeSpan.FromMinutes(5), Token);
            }

            return (lease ? "leases" : "coordination_values", name, null);
        }
        catch (GatewayException failed)
        {
            // A write that failed with no unit failure recorded did not lose a race in its unit, so it fails the test
            // rather than skipping the code assertion (ISK-0009 T074).
            var failure = isolating.Opened.Select(unit => unit.Failure).FirstOrDefault(failure => failure is not null);
            Assert.True(failure is not null, $"The write failed with {failed.Code.Id}, and none of its units recorded a failure.");
            return (lease ? "leases" : "coordination_values", name, failure);
        }
    }

    private static string ScopeKey(CoordinationScope scope) => CoordinationKey.ScopeKey(scope.Owner);

    // Opens every unit at one isolation level and keeps each unit, so a test can read the failure it recorded.
    private sealed class IsolatingUnits(IUnitOfWorkFactory inner, UnitOfWorkIsolation isolation) : IUnitOfWorkFactory
    {
        public ConcurrentQueue<IUnitOfWork> Opened { get; } = new();

        public async ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken)
        {
            var unit = await inner.OpenAsync(scope, options with { Isolation = isolation }, cancellationToken);
            Opened.Enqueue(unit);
            return unit;
        }
    }
}

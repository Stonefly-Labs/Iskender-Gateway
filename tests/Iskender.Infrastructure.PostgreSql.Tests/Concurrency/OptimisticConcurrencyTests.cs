using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

namespace Iskender.Infrastructure.PostgreSql.Tests.Concurrency;

/// <summary>User Story 2 scenario 1 and FR-016: a stale version token changes nothing and reads as a conflict.</summary>
public class OptimisticConcurrencyTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private const int Writers = 20;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OfTwentyWritersFromOneVersionExactlyOneWins()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["poolMaximum"] = Writers + 5 });
        var id = await SeedAsync(store);
        var widgets = new WidgetRepository();
        var allRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, Writers).Select(writer => Task.Run(async () =>
        {
            await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
            var version = new VersionToken((await widgets.GetAsync(unit, id, Token)).RowVersion);
            if (Interlocked.Increment(ref reads) == Writers)
            {
                allRead.SetResult();
            }

            await allRead.Task.WaitAsync(Token);
            try
            {
                var next = await WidgetRepository.RenameVersionedAsync(unit, id, version, $"writer-{writer}", Token);
                await unit.CommitAsync(Token);
                return (Writer: writer, Read: version, Next: (VersionToken?)next, Failure: (GatewayException?)null, State: unit.State, Class: unit.Failure?.FailureClass);
            }
            catch (GatewayException failure)
            {
                return (writer, version, null, failure, unit.State, unit.Failure?.FailureClass);
            }
        }, Token)));

        Assert.All(outcomes, outcome => Assert.Equal(VersionToken.Initial, outcome.Read));
        var winner = Assert.Single(outcomes, outcome => outcome.Failure is null);
        var losers = outcomes.Where(outcome => outcome.Failure is not null).ToList();
        Assert.Equal(Writers - 1, losers.Count);
        Assert.All(losers, loser =>
        {
            Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, loser.Failure!.Code);
            Assert.Equal(UnitOfWorkState.Faulted, loser.State);
            Assert.Equal(PersistenceFailureClass.ConcurrencyConflict, loser.Class);
        });
        Assert.Equal(VersionToken.Initial.Next(), winner.Next);

        await using var check = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var row = await widgets.GetAsync(check, id, Token);
        Assert.Equal(($"writer-{winner.Writer}", 2L), (row.Name, row.RowVersion));
    }

    [Fact]
    public async Task AStaleTokenConflictsWhileAForeignOrMissingRowReadsAsNotFound()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var id = await SeedAsync(store);

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.Equal(new VersionToken(2), await WidgetRepository.RenameVersionedAsync(unit, id, VersionToken.Initial, "second", Token));
            await unit.CommitAsync(Token);
        }

        await using (var foreign = await store.OpenAsync(Owners.Organization(Owners.OrgB)))
        {
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.RenameVersionedAsync(foreign, id, VersionToken.Initial, "stolen", Token))).Code);
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.RenameVersionedAsync(foreign, id, new VersionToken(2), "stolen", Token))).Code);
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.DeleteVersionedAsync(foreign, id, new VersionToken(2), Token))).Code);
            Assert.Equal(UnitOfWorkState.Open, foreign.State);
        }

        await using (var stale = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.DeleteVersionedAsync(stale, id, VersionToken.Initial, Token))).Code);
            Assert.Equal(UnitOfWorkState.Faulted, stale.State);
        }

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(async () => await WidgetRepository.RenameVersionedAsync(unit, id + "-missing", VersionToken.Initial, "x", Token))).Code);
            await WidgetRepository.DeleteVersionedAsync(unit, id, new VersionToken(2), Token);
            Assert.Same(ErrorCodes.ResourceNotFound, (await Assert.ThrowsAsync<GatewayException>(() => new WidgetRepository().GetAsync(unit, id, Token))).Code);
            await unit.CommitAsync(Token);
        }

        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM isk_alpha.widgets WHERE id = '{id}'"));
    }

    private static async Task<string> SeedAsync(TestStore store)
    {
        var id = $"w-occ-{Guid.NewGuid():N}";
        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        await WidgetRepository.InsertAsync(unit, id, "first", Token);
        await unit.CommitAsync(Token);
        return id;
    }
}

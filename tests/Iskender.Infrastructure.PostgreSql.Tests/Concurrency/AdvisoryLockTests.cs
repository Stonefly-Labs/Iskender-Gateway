using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

namespace Iskender.Infrastructure.PostgreSql.Tests.Concurrency;

/// <summary>User Story 2 scenarios 4 and 5, FR-027 and FR-028: transaction-scoped advisory locks keyed by the unit's own scope.</summary>
public class AdvisoryLockTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private const int Holders = 10;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HoldersOfOneKeyNeverInterleave()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["poolMaximum"] = Holders + 2 });
        var name = Name();
        var run = Guid.NewGuid().ToString("N")[..8];

        await Task.WhenAll(Enumerable.Range(0, Holders).Select(holder => Task.Run(async () =>
        {
            await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
            Assert.True(await unit.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.Bounded(TimeSpan.FromSeconds(30)), Token));
            await LockLogRepository.AppendAsync(unit, $"{run}-{holder}-start", Token);
            await Task.Delay(20, Token);
            await LockLogRepository.AppendAsync(unit, $"{run}-{holder}-end", Token);
            await unit.CommitAsync(Token);
        }, Token)));

        await using var reader = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var log = (await LockLogRepository.ReadAsync(reader, Token)).Where(entry => entry.StartsWith(run, StringComparison.Ordinal)).ToList();
        Assert.Equal(2 * Holders, log.Count);
        for (var i = 0; i < log.Count; i += 2)
        {
            var holder = log[i][..log[i].LastIndexOf('-')];
            Assert.Equal((holder + "-start", holder + "-end"), (log[i], log[i + 1]));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLockIsGoneAfterCommitOrRollback(bool commit)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var name = Name();
        var key = AdvisoryLockKey.Derive(Owners.Organization(Owners.OrgA).Owner, name.Module, name.Name);

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.True(await unit.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
            Assert.Equal(1, await LocksAsync(key));
            if (commit)
            {
                await unit.CommitAsync(Token);
            }
        }

        Assert.Equal(0, await LocksAsync(key));
    }

    [Fact]
    public async Task TwoOrganizationsWithTheSameModuleAndNameDoNotWaitForEachOther()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var name = Name();

        await using var first = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        await using var second = await store.OpenAsync(Owners.Organization(Owners.OrgB));
        await using var sameScope = await store.OpenAsync(Owners.Organization(Owners.OrgA));

        Assert.True(await first.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
        Assert.True(await second.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
        Assert.False(await sameScope.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
    }

    [Fact]
    public async Task ATryOnlyMissLeavesTheUnitOpenAndAbleToCommit()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var name = Name();
        var counter = $"c-lock-{Guid.NewGuid():N}";

        await using var holder = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        Assert.True(await holder.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            Assert.False(await unit.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));
            Assert.Equal(UnitOfWorkState.Open, unit.State);
            await CounterRepository.CreateAsync(unit, counter, Token);
            await unit.CommitAsync(Token);
        }

        await using var reader = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        Assert.Equal(0, await new CounterRepository().GetAsync(reader, counter, Token));
    }

    [Fact]
    public async Task AnExpiredBoundedWaitFaultsTheUnitWithALockTimeout()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var name = Name();

        await using var holder = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        Assert.True(await holder.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.TryOnly, Token));

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await unit.AcquireAdvisoryLockAsync(name, AdvisoryLockWait.Bounded(TimeSpan.FromMilliseconds(300)), Token));

        Assert.Same(ErrorCodes.PersistenceLockTimeout, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.Same(ErrorCodes.PersistenceLockTimeout, unit.Failure!.Code);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4006);
        Assert.Equal(UnitOfWorkState.Open, holder.State);
    }

    [Fact]
    public async Task ABoundedWaitThatSucceedsRestoresThePreviousLockTimeout()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var adapter = PostgreSqlUnitOfWork.From(unit);
        await adapter.FrameworkCommand("SELECT set_config('lock_timeout', '7s', true)").ExecuteAsync(Token);

        Assert.True(await unit.AcquireAdvisoryLockAsync(Name(), AdvisoryLockWait.Bounded(TimeSpan.FromSeconds(2)), Token));

        Assert.Equal("7s", await adapter.FrameworkCommand("SELECT current_setting('lock_timeout')").ScalarAsync<string>(Token));
        Assert.Equal(UnitOfWorkState.Open, unit.State);
    }

    private static AdvisoryLockName Name() => new("beta", "lock." + Guid.NewGuid().ToString("N")[..12]);

    // Advisory locks on the key, as pg_locks shows a bigint key: the high and low 32 bits, objsubid 1.
    private async Task<long> LocksAsync(long key) =>
        await database.Fixture.ScalarAsync<long>(
            database.Name,
            $"SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND objsubid = 1 AND classid = {(uint)(key >> 32)} AND objid = {unchecked((uint)key)}");
}

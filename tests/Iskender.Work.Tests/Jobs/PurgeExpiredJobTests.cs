using System.Reflection;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Jobs;

/// <summary>
/// The shipped job <c>coordination.purge_expired</c> (ISK-0009 T042, US5.1, SC-006, <c>work</c> contract Recurring jobs
/// rule 4): expired atomic-operation and lease rows in three scopes are deleted, every fence record and namespace
/// generation remains, and each scope's <c>coordination</c> directory row is removed once nothing expiring remains.
/// </summary>
public class PurgeExpiredJobTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const string Coordination =
        "\"leases\":{\"coordinator\":\"postgresql\",\"minimumDuration\":\"300ms\",\"maximumDuration\":\"1m\",\"safetyMargin\":\"100ms\"}";

    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(300);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExpiredRowsInThreeScopesGoFencesAndGenerationsStayAndEachEmptiedScopeLosesItsRow()
    {
        work.RequirePostgreSql();
        var (_, settings) = WorkTestSettings.Read(coordination: Coordination);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(work.Database), TestCredentialProvider.For(work.Postgres));
        var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, work.Logger);
        var atomic = new PostgreSqlAtomicOperations(units, settings, work.Logger);
        var leases = new PostgreSqlLeaseCoordinator(units, settings, work.Logger);
        var scopes = new[] { WorkFixture.FreshOrganization(), WorkFixture.FreshOrganization(), Owners.Project(Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Organization), Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Project)) }
            .Select(CoordinationScope.From).ToList();

        // Every scope gets expiring rows, a fence, and a generation; the last keeps a live value.
        foreach (var scope in scopes)
        {
            for (var index = 0; index < 3; index++)
            {
                await atomic.AddAsync(scope, Name("expired_" + index), 1, Brief, Token);
            }

            var lease = (await leases.AcquireAsync(scope, Name("expired_lease"), Brief, LeaseWait.TryOnly, Token)).Lease!;
            await using (var unit = await units.OpenAsync(scope.UnitScope, new UnitOfWorkOptions("work", "test.fence"), Token))
            {
                await new PostgreSqlFencingGuard(work.Logger).AcceptAsync(unit, Name("resource"), lease, Token);
                await unit.CommitAsync(Token);
            }

            await new PostgreSqlVersionedCache(units, settings, work.Logger).InvalidateAsync(scope, new CacheNamespace("purge", "entries"), Token);
        }

        await atomic.AddAsync(scopes[2], Name("live"), 1, TimeSpan.FromMinutes(5), Token);
        await Eventually.HoldsAsync(
            async () => await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.coordination_values WHERE expires_at <= clock_timestamp() AND scope_key IN ({Keys(scopes)})") == 9
                && await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE expires_at <= clock_timestamp() AND scope_key IN ({Keys(scopes)})") == 3,
            "the brief rows to expire");
        var fences = await CountAsync("fences", scopes);
        var generations = await CountAsync("cache_generations", scopes);
        string? saved = null;

        // A batch of 2 makes each scope take more than one purge, and the three scopes more than one page.
        var outcome = await new PurgeExpiredJob(units, settings).RunAsync(new RecurringJobContext(2, null, (cursor, _) => { saved = cursor; return ValueTask.CompletedTask; }), Token);

        Assert.Equal(RecurringJobOutcome.Completed, outcome);
        Assert.NotNull(saved);
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.coordination_values WHERE expires_at <= clock_timestamp() AND scope_key IN ({Keys(scopes)})"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE scope_key IN ({Keys(scopes)})"));
        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.coordination_values WHERE scope_key IN ({Keys(scopes)})"));
        Assert.Equal(fences, await CountAsync("fences", scopes));
        Assert.Equal(generations, await CountAsync("cache_generations", scopes));
        Assert.Equal(3L, fences);
        Assert.Equal(3L, generations);

        // Only the scope with a live value keeps its row, due when that value expires.
        Assert.Equal(
            $"{Key(scopes[2])}|later",
            await work.ScalarAsync<string>($"SELECT coalesce(string_agg(listed_scope || '|' || CASE WHEN due_at > clock_timestamp() THEN 'later' ELSE 'due' END, ','), '') FROM isk_directory.scopes WHERE module = 'coordination' AND listed_scope IN ({Keys(scopes)})"));
    }

    // A lease renewed to a shorter duration moves its expiry earlier than its scope's directory due time, which follows it
    // down, so the purge reaches the row once it expires rather than at the old due time (ISK-0009 T094, FR-020, coordination
    // contract Atomic operations rules 6 and 7, US5.1, SC-006; decided by the maintainer in implement on 2026-11-19).
    [Fact]
    public async Task ALeaseRenewedShortIsPurgedOnceItExpires()
    {
        work.RequirePostgreSql();
        var (_, settings) = WorkTestSettings.Read(coordination: Coordination);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(work.Database), TestCredentialProvider.For(work.Postgres));
        var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, work.Logger);
        var leases = new PostgreSqlLeaseCoordinator(units, settings, work.Logger);
        var scope = CoordinationScope.From(WorkFixture.FreshOrganization());
        var lease = (await leases.AcquireAsync(scope, Name("renewed_short"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        await leases.RenewAsync(lease, Brief, Token);
        await Eventually.HoldsAsync(
            async () => await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE expires_at <= clock_timestamp() AND scope_key = '{Key(scope)}'") == 1,
            "the renewed lease to expire");

        var outcome = await new PurgeExpiredJob(units, settings).RunAsync(new RecurringJobContext(500, null, (_, _) => ValueTask.CompletedTask), Token);

        Assert.Equal(RecurringJobOutcome.Completed, outcome);
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE scope_key = '{Key(scope)}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'coordination' AND listed_scope = '{Key(scope)}'"));
    }

    // The placeholder a release of an unknown acquire writes updates this holder's existing row to expire now, earlier than
    // the scope's directory due time, which follows it down, so the purge reaches the row (ISK-0009 T100, the T094
    // decision, coordination contract Atomic operations rule 7, FR-020). The insert branch is DirectoryRegistrationTests'.
    [Fact]
    public async Task AReleasePlaceholderThatUpdatesALongLeaseLowersTheDueTimeAndIsPurged()
    {
        work.RequirePostgreSql();
        var (_, settings) = WorkTestSettings.Read(coordination: Coordination);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(work.Database), TestCredentialProvider.For(work.Postgres));
        var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, work.Logger);
        var leases = new PostgreSqlLeaseCoordinator(units, settings, work.Logger);
        var scope = CoordinationScope.From(WorkFixture.FreshOrganization());
        var lease = (await leases.AcquireAsync(scope, Name("placeholder"), TimeSpan.FromMinutes(1), LeaseWait.TryOnly, Token)).Lease!;
        var due = $"SELECT due_at FROM isk_directory.scopes WHERE module = 'coordination' AND listed_scope = '{Key(scope)}'";
        Assert.True(await work.ScalarAsync<DateTime>(due) > await work.ScalarAsync<DateTime>("SELECT clock_timestamp() + interval '30 seconds'"));

        // The release of an acquire whose outcome is unknown runs only after a cut-off commit, so it is called directly,
        // for the existing row's holder.
        var release = typeof(PostgreSqlLeaseCoordinator).GetMethod("ReleaseHolderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (ValueTask)release.Invoke(leases, [scope, "purge", CoordinationKey.Hash(scope.Owner, "purge", "placeholder"), lease.Holder])!;

        Assert.Equal(1L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE scope_key = '{Key(scope)}' AND holder = '{lease.Holder}' AND expires_at <= clock_timestamp()"));
        Assert.Equal(await work.ScalarAsync<DateTime>($"SELECT expires_at FROM isk_coordination.leases WHERE scope_key = '{Key(scope)}'"), await work.ScalarAsync<DateTime>(due));

        var outcome = await new PurgeExpiredJob(units, settings).RunAsync(new RecurringJobContext(500, null, (_, _) => ValueTask.CompletedTask), Token);

        Assert.Equal(RecurringJobOutcome.Completed, outcome);
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.leases WHERE scope_key = '{Key(scope)}'"));
        Assert.Equal(0L, await work.ScalarAsync<long>($"SELECT count(*) FROM isk_directory.scopes WHERE module = 'coordination' AND listed_scope = '{Key(scope)}'"));
    }

    private static CoordinationName Name(string name) => new("purge", name);

    private static string Key(CoordinationScope scope) => CoordinationKey.ScopeKey(scope.Owner);

    private static string Keys(IEnumerable<CoordinationScope> scopes) => string.Join(", ", scopes.Select(scope => $"'{Key(scope)}'"));

    private Task<long> CountAsync(string table, IEnumerable<CoordinationScope> scopes) =>
        work.ScalarAsync<long>($"SELECT count(*) FROM isk_coordination.{table} WHERE scope_key IN ({Keys(scopes)})");
}

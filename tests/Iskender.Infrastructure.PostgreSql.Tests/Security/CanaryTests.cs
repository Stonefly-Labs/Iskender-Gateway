using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Security;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceDiagnostics = Iskender.Coordination.Conformance.CapturingDiagnostics;

namespace Iskender.Infrastructure.PostgreSql.Tests.Security;

/// <summary>
/// The canary check against the PostgreSQL coordinator (SC-005, FR-004, FR-026): with the canary as the organization ID,
/// a cached value, and a failing credential provider's message, every port operation, a stopped server, a refused
/// credential, a fencing rejection, a lease loss, two capability refusals, and the health probe report it 0 times in
/// results, <c>ToString()</c> output, exceptions and their diagnostics, log messages and fields, metric labels, and
/// activity tags; and no key or hash column in <c>isk_coordination</c> holds it. Only <c>scope_key</c>, the row-level
/// security column FR-004 permits, carries the organization.
/// </summary>
public class CanaryTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCanaryNeverAppearsInWhatThePostgreSqlCoordinatorReportsOrKeys()
    {
        coordinator.Require();
        var scopes = CanaryDriver.Scopes();
        var scope = scopes.OrganizationA;
        var client = coordinator.CreateClient(0);
        var duration = coordinator.Settings.MinimumLeaseDuration;
        var logger = coordinator.Logger;

        await CanaryDriver.DriveAsync(client, scope, duration, Token);
        CapturingDiagnostics().Report(await new PostgreSqlCoordinationHealth(coordinator.Units(0), coordinator.CoordinationSettings, logger).CheckAsync(Token));

        // A lease that expires is lost.
        var lost = (await client.Leases.AcquireAsync(scope, new CoordinationName(CanaryDriver.Module, "lost"), duration, LeaseWait.TryOnly, Token)).Lease!;
        await coordinator.PassTimeAsync(duration + TimeSpan.FromMilliseconds(100));
        Assert.Same(ErrorCodes.CoordinationLeaseLost, (await CanaryDriver.FailAsync(async () => await client.Leases.RenewAsync(lost, duration, Token))).Code);

        // A stale token is rejected by the guard.
        var resource = new CoordinationName(CanaryDriver.Module, "fenced");
        var older = (await client.Leases.AcquireAsync(scope, resource, duration, LeaseWait.TryOnly, Token)).Lease!;
        await client.Leases.ReleaseAsync(older, Token);
        var newer = (await client.Leases.AcquireAsync(scope, resource, duration, LeaseWait.TryOnly, Token)).Lease!;
        await using (var unit = await OpenAsync(scope))
        {
            await coordinator.Guard.AcceptAsync(unit, resource, newer, Token);
            await unit.CommitAsync(Token);
        }

        await using (var unit = await OpenAsync(scope))
        {
            Assert.Same(ErrorCodes.CoordinationFencingRejected, (await CanaryDriver.FailAsync(async () => await coordinator.Guard.AcceptAsync(unit, resource, older, Token))).Code);
        }

        await client.Leases.ReleaseAsync(newer, Token);

        // A lease the PostgreSQL coordinator did not issue, and a feature the checker refused.
        var local = (await new LocalLeaseCoordinator(coordinator.CoordinationSettings, logger).AcquireAsync(scope, resource, duration, LeaseWait.TryOnly, Token)).Lease!;
        await using (var unit = await OpenAsync(scope))
        {
            Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, (await CanaryDriver.FailAsync(async () => await coordinator.Guard.AcceptAsync(unit, resource, local, Token))).Code);
        }

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, (await CanaryDriver.RefuseAsync(client, scope, Token)).Code);

        // A credential provider that fails with the canary in its message.
        await using (var store = TestStore.For(coordinator.Database))
        {
            store.Credentials.Failure = new InvalidOperationException($"The vault refused {Canary.Value} for {Canary.OrganizationId}.");
            var atomic = new PostgreSqlAtomicOperations(store.Factory, coordinator.CoordinationSettings);
            Assert.Same(ErrorCodes.CoordinationUnavailable, (await CanaryDriver.FailAsync(async () => await atomic.ReserveAsync(scope, new CoordinationName(CanaryDriver.Module, "counter"), 1, 10, Expiry, Token))).Code);
            var refused = await new PostgreSqlCoordinationHealth(store.Factory, coordinator.CoordinationSettings, logger).CheckAsync(Token);
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed), refused);
            CapturingDiagnostics().Report(refused);
        }

        // No key or hash column holds the canary; scope_key, the row-level security column, does.
        Assert.True(await CountAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'isk_coordination' AND column_name LIKE '%\\_hash'") >= 4);
        Assert.Equal(0, await HashColumnsHoldingCanaryAsync());
        Assert.True(await CountAsync($"SELECT count(*) FROM isk_coordination.coordination_values WHERE scope_key LIKE '%{Canary.Value}%'") > 0);

        Assert.NotEmpty(logger.Events);
        Assert.Empty(CapturingDiagnostics().CanaryHits());
    }

    [Fact]
    public async Task TheCanaryNeverAppearsWhenTheServerIsStopped()
    {
        coordinator.Require();

        // A container of its own, so stopping it leaves every other class's database alone.
        await using var fixture = new PostgreSqlFixture();
        await fixture.InitializeAsync();
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        await using var store = TestStore.For(database);
        var settings = PostgreSqlCoordinatorUnderTest.ReadSettings("\"operationTimeout\":\"1s\",\"healthProbeTimeout\":\"1s\"");
        var logger = new ConformanceDiagnostics.Logger();
        var scope = CanaryDriver.Scopes().OrganizationA;
        var atomic = new PostgreSqlAtomicOperations(store.Factory, settings);
        var cache = new PostgreSqlVersionedCache(store.Factory, settings, logger);
        var counter = new CoordinationName(CanaryDriver.Module, "counter");
        CapturingDiagnostics().Report(await atomic.AddAsync(scope, counter, 1, Expiry, Token));

        await fixture.StopAsync();
        try
        {
            Assert.Same(ErrorCodes.CoordinationUnavailable, (await CanaryDriver.FailAsync(async () => await atomic.ReserveAsync(scope, counter, 1, 10, Expiry, Token))).Code);
            CapturingDiagnostics().Report(await cache.TryGetAsync(scope, new CacheNamespace(CanaryDriver.Module, "entries"), "entry", 1, Token));
            CapturingDiagnostics().Report(await new PostgreSqlCoordinationHealth(store.Factory, settings, logger).CheckAsync(Token));
        }
        finally
        {
            // Started again, so the scratch database can be dropped before the container is removed.
            await fixture.RestartAsync();
        }

        Assert.Contains(logger.Events, logged => logged.Id.Id == 5008);
        Assert.Empty(CapturingDiagnostics().CanaryHits());
    }

    private static ConformanceDiagnostics CapturingDiagnostics() => ConformanceDiagnostics.Shared;

    private ValueTask<IUnitOfWork> OpenAsync(CoordinationScope scope) =>
        coordinator.Units(1).OpenAsync(scope.UnitScope, new UnitOfWorkOptions("scoped", "canary.fence"), Token);

    private Task<long> CountAsync(string sql) => coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, sql);

    // As the superuser, past row-level security: the rows whose key or hash columns hold the canary, in every table.
    private Task<long> HashColumnsHoldingCanaryAsync() => CountAsync(
        "SELECT coalesce(sum((xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM isk_coordination.%I WHERE %I::text ILIKE %L', "
        + $"table_name, column_name, '%{Canary.Value}%'), false, true, '')))[1]::text::bigint), 0)::bigint "
        + "FROM information_schema.columns WHERE table_schema = 'isk_coordination' AND column_name LIKE '%\\_hash'");
}

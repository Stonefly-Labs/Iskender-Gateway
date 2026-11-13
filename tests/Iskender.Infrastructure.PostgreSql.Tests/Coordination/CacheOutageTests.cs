using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceDiagnostics = Iskender.Coordination.Conformance.CapturingDiagnostics;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// User Story 4 scenario 3 against PostgreSQL (FR-020, research R13): with the server stopped, a read is a miss within the
/// operation bound, writes event 5008, and throws nothing, even for an entry held in memory, because its generation
/// cannot be checked; a failed set or invalidation throws, so a lost invalidation is visible.
/// </summary>
public class CacheOutageTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    // Covers scheduling and the driver noticing a closed socket.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly CacheNamespace _namespace = new("cache_outage", "entries");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario3AReadDuringAnOutageIsAMissAndAFailedWriteThrows()
    {
        coordinator.Require();

        // A container of its own, so stopping it leaves every other class's database alone.
        await using var fixture = new PostgreSqlFixture();
        await fixture.InitializeAsync();
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        await using var store = TestStore.For(database);
        var settings = PostgreSqlCoordinatorUnderTest.ReadSettings("\"operationTimeout\":\"1s\"");
        var logger = new ConformanceDiagnostics.Logger();
        var cache = new PostgreSqlVersionedCache(store.Factory, settings, logger);
        var scope = new ConformanceScopes().OrganizationA;
        var stamp = (await cache.TryGetAsync(scope, _namespace, "item", 1, Token)).Stamp;
        Assert.True(await cache.SetAsync(scope, _namespace, "item", 1, new byte[] { 1 }, Expiry, stamp, Token));
        Assert.True((await cache.TryGetAsync(scope, _namespace, "item", 1, Token)).IsHit);

        await fixture.StopAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            var lookup = await cache.TryGetAsync(scope, _namespace, "item", 1, Token);
            var elapsed = watch.Elapsed;

            Assert.False(lookup.IsHit);
            Assert.Null(lookup.Stamp);
            Assert.InRange(elapsed, TimeSpan.Zero, settings.OperationTimeout + Allowance);
            Assert.Contains(logger.Events, logged => logged.Id.Id == 5008);

            var set = await Assert.ThrowsAsync<GatewayException>(async () => await cache.SetAsync(scope, _namespace, "item", 2, new byte[] { 2 }, Expiry, stamp, Token));
            Assert.Same(ErrorCodes.CoordinationUnavailable, set.Code);
            var bump = await Assert.ThrowsAsync<GatewayException>(async () => await cache.InvalidateAsync(scope, _namespace, Token));
            Assert.Same(ErrorCodes.CoordinationUnavailable, bump.Code);
        }
        finally
        {
            // Started again, so the scratch database can be dropped before the container is removed.
            await fixture.RestartAsync();
        }
    }
}

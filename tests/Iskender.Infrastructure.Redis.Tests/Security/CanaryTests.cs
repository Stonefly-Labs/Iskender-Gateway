using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Security;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Security;

/// <summary>
/// The canary check against Redis (SC-005, User Story 5 scenario 2, FR-004, FR-026). On a container of its own, the canary
/// is the server's network alias (and the primary host it reports as a replica), the ACL user name, the password, the
/// organization ID, a cached value, and a failing credential provider's message. Every port operation, a lease loss, a
/// capability refusal, a refused credential, a stopped, paused, and read-only server, and the health probe report it 0
/// times in results, <c>ToString()</c> output, exceptions and their diagnostics, log messages and fields, metric labels,
/// and activity tags; and no key the server holds, read with <c>SCAN</c> and with <c>KEYS</c>, contains it.
/// </summary>
public class CanaryTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CapturingDiagnostics Diagnostics => CapturingDiagnostics.Shared;

    [Fact]
    public async Task TheCanaryNeverAppearsInWhatTheRedisAdapterReportsOrKeys()
    {
        await using var redis = new RedisFixture(Canary.Value, Canary.Value, Canary.Value);
        await redis.InitializeAsync();
        redis.RequireRedis();
        var logger = new CapturingDiagnostics.Logger();
        var credentials = TestCoordinationCredentialProvider.For(redis);
        var settings = TestCoordinationSettings.For(
            redis,
            redisOverrides: new Dictionary<string, object> { ["connectTimeout"] = "1s" },
            sectionOverrides: new Dictionary<string, object> { ["operationTimeout"] = "1s", ["healthProbeTimeout"] = "1s" });
        await using var connection = new RedisConnection(settings, credentials, logger);
        var client = new CoordinatorClient(new RedisAtomicOperations(connection), new RedisLeaseCoordinator(connection), new RedisVersionedCache(connection));
        var probe = new RedisHealthProbe(connection);
        var scope = CanaryDriver.Scopes().OrganizationA;
        var counter = new CoordinationName(CanaryDriver.Module, "counter");
        var duration = settings.MinimumLeaseDuration;

        await CanaryDriver.DriveAsync(client, scope, duration, Token);
        Assert.Equal(CoordinationHealthResult.Healthy, await ReportAsync(probe));

        // A lease that expires is lost, and a feature the checker refused never reaches a port.
        var lost = (await client.Leases.AcquireAsync(scope, new CoordinationName(CanaryDriver.Module, "lost"), duration, LeaseWait.TryOnly, Token)).Lease!;
        await Task.Delay(duration + TimeSpan.FromMilliseconds(200), Token);
        Assert.Same(ErrorCodes.CoordinationLeaseLost, (await CanaryDriver.FailAsync(async () => await client.Leases.RenewAsync(lost, duration, Token))).Code);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, (await CanaryDriver.RefuseAsync(client, scope, Token)).Code);

        // A wrong password, and a provider that fails with the canary in its message, each on a connection of its own.
        foreach (var refusal in new[] { (Override: Canary.Value + "x", Failure: (Exception?)null), (Override: (string?)null, Failure: new InvalidOperationException($"The vault refused {Canary.Value}.")) })
        {
            var refused = TestCoordinationCredentialProvider.For(redis);
            refused.Override = refusal.Override;
            refused.Failure = refusal.Failure;
            await using var other = new RedisConnection(settings, refused, logger);
            Assert.Same(ErrorCodes.CoordinationUnavailable, (await CanaryDriver.FailAsync(async () => await new RedisAtomicOperations(other).AddAsync(scope, counter, 1, Expiry, Token))).Code);
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed), await ReportAsync(new RedisHealthProbe(other)));
        }

        // The server as a replica of its own canary alias, which ROLE reports.
        await redis.BecomeReplicaAsync(Canary.Value, RedisFixture.ServerPort);
        try
        {
            Assert.Same(ErrorCodes.CoordinationUnavailable, (await CanaryDriver.FailAsync(async () => await client.Atomic.AddAsync(scope, counter, 1, Expiry, Token))).Code);
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly), await ReportAsync(probe));
            var role = (RedisResult[])(await (await redis.AdminAsync()).ExecuteAsync("ROLE"))!;
            Assert.True(Canary.In((string?)role[1]), "The replica does not report its canary primary.");
        }
        finally
        {
            await redis.BecomePrimaryAsync();
        }

        // Read while the server serves: the key space holds no canary, though the module and the value are there.
        var admin = await redis.AdminAsync();
        var scanned = new List<string>();
        await foreach (var key in admin.KeysAsync(pattern: "*", pageSize: 100))
        {
            scanned.Add(key.ToString());
        }

        var listed = ((RedisResult[])(await admin.ExecuteAsync("KEYS", "*"))!).Select(key => (string)key!).ToList();
        Assert.Contains(scanned, key => key.StartsWith("isk:v1:" + CanaryDriver.Module + ":", StringComparison.Ordinal));
        Assert.Equal(scanned.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(scanned, Canary.In);
        Assert.Contains(Canary.Value, ((string?)await admin.ExecuteAsync("ACL", "WHOAMI")) + string.Join(" ", ((RedisResult[])(await admin.ExecuteAsync("ACL", "USERS"))!).Select(user => (string?)user)), StringComparison.Ordinal);

        await redis.PauseAsync();
        try
        {
            Assert.Same(ErrorCodes.CoordinationTimeout, (await CanaryDriver.FailAsync(async () => await client.Atomic.ReserveAsync(scope, counter, 1, 10, Expiry, Token))).Code);
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout), await ReportAsync(probe));
        }
        finally
        {
            await redis.UnpauseAsync();
        }

        await redis.StopAsync();
        try
        {
            Assert.Same(ErrorCodes.CoordinationUnavailable, (await CanaryDriver.FailAsync(async () => await client.Atomic.ReadAsync(scope, counter, Token))).Code);
            Diagnostics.Report(await client.Cache.TryGetAsync(scope, new CacheNamespace(CanaryDriver.Module, "entries"), "entry", 1, Token));
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable), await ReportAsync(probe));
        }
        finally
        {
            await redis.StartAsync();
        }

        Assert.NotEmpty(logger.Events);
        Assert.Empty(Diagnostics.CanaryHits());
    }

    private static async Task<CoordinationHealthResult> ReportAsync(RedisHealthProbe probe)
    {
        var result = await probe.CheckAsync(Token);
        Diagnostics.Report(result);
        return result;
    }
}

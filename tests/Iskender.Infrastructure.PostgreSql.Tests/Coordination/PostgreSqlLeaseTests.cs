using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

public class PostgreSqlLeaseTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TokensIncreaseStrictlyAcrossKeysAndClients()
    {
        coordinator.Require();
        var module = Module();
        var tokens = new List<long>();

        for (var index = 0; index < 10; index++)
        {
            var leases = coordinator.Leases(index % 2);
            tokens.Add((await leases.AcquireAsync(coordinator.Scopes.OrganizationA, new CoordinationName(module, $"key_{index}"), Duration, LeaseWait.TryOnly, Token)).Lease!.Token.Value);
        }

        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public async Task NoAdvisoryLockIsHeldDuringAcquireRenewOrRelease()
    {
        coordinator.Require();
        var module = Module();
        var scope = coordinator.Scopes.OrganizationA;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var samples = 0;
        var held = 0L;
        var sampler = Task.Run(async () =>
        {
            await using var connection = await coordinator.Fixture.OpenSuperuserAsync(coordinator.Database.Name);
            await using var command = new Npgsql.NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
                connection);
            while (!stop.IsCancellationRequested)
            {
                held = Math.Max(held, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
                samples++;
            }
        }, CancellationToken.None);

        for (var round = 0; round < 20; round++)
        {
            var leases = coordinator.Leases(round % 2);
            var name = new CoordinationName(module, $"key_{round % 3}");
            var lease = (await leases.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
            lease = await leases.RenewAsync(lease, Duration, Token);
            await leases.ReleaseAsync(lease, Token);
        }

        await stop.CancelAsync();
        await sampler;
        Assert.True(samples > 0);
        Assert.Equal(0, held);
    }

    [Fact]
    public async Task TokensKeepIncreasingAcrossARestart()
    {
        coordinator.Require();

        // A container of its own, so restarting it leaves every other class's database alone.
        await using var fixture = new PostgreSqlFixture();
        await fixture.InitializeAsync();
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        var scopes = new ConformanceScopes();
        var name = new CoordinationName(Module(), "restart");

        var before = new List<long>();
        await using (var store = TestStore.For(database))
        {
            var leases = new PostgreSqlLeaseCoordinator(store.Factory, coordinator.CoordinationSettings);
            for (var index = 0; index < 3; index++)
            {
                var lease = (await leases.AcquireAsync(scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
                before.Add(lease.Token.Value);
                await leases.ReleaseAsync(lease, Token);
            }
        }

        await fixture.RestartAsync();

        var after = new List<long>();
        await using (var store = TestStore.For(database))
        {
            var leases = new PostgreSqlLeaseCoordinator(store.Factory, coordinator.CoordinationSettings);
            for (var index = 0; index < 3; index++)
            {
                var lease = (await leases.AcquireAsync(scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
                after.Add(lease.Token.Value);
                await leases.ReleaseAsync(lease, Token);
            }
        }

        var tokens = before.Concat(after).ToList();
        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.True(after.Min() > before.Max());
    }

    private static string Module() => "pgl_" + Guid.NewGuid().ToString("N")[..12];
}

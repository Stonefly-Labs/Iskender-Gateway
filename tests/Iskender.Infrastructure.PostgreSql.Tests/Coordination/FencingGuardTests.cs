using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Scoped;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

public class FencingGuardTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly string _module = "fg_" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => coordinator.Scopes.OrganizationA;

    private CoordinationName Resource => new(_module, "resource");

    [Fact]
    public async Task TheFirstTokenIsAcceptedAndRecorded()
    {
        coordinator.Require();
        var lease = await AcquireAsync("lease");

        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Resource, lease, Token));

        Assert.Equal(lease.Token.Value, await HighestTokenAsync());
        await ReleaseAsync(lease);
    }

    [Fact]
    public async Task AnEqualTokenIsAccepted()
    {
        coordinator.Require();
        var lease = await AcquireAsync("lease");
        var fence = new PostgreSqlFence(coordinator);

        Assert.True(await fence.WriteAsync(Resource, lease, Token));
        Assert.True(await fence.WriteAsync(Resource, lease, Token));

        Assert.Equal([lease.Token.Value, lease.Token.Value], await fence.CommittedTokensAsync(Scope, Resource, Token));
        await ReleaseAsync(lease);
    }

    [Fact]
    public async Task ALowerTokenFaultsTheUnitAndLeavesTheProtectedRowUnchanged()
    {
        coordinator.Require();
        var older = await AcquireAsync("lease");
        await ReleaseAsync(older);
        var newer = await AcquireAsync("lease");
        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Resource, newer, Token));
        var seen = coordinator.Logger.Events.Count;

        await using (var unit = await OpenAsync(Scope))
        {
            var rejected = await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Guard.AcceptAsync(unit, Resource, older, Token));
            Assert.Same(ErrorCodes.CoordinationFencingRejected, rejected.Code);
            Assert.Equal(CoordinationFailureClass.FencingRejected, CoordinationFailures.ClassOf(rejected));
            Assert.Equal(UnitOfWorkState.Faulted, unit.State);
            await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, "INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ($1, $2, $3)")
                .With(PostgreSqlFence.Protected(Resource) + "#stale").With(older.Token.Value).ExecuteAsync(Token));
            await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token));
        }

        Assert.Equal([newer.Token.Value], await new PostgreSqlFence(coordinator).CommittedTokensAsync(Scope, Resource, Token));
        Assert.Equal(newer.Token.Value, await HighestTokenAsync());
        var rejection = Assert.Single(coordinator.Logger.Events.Skip(seen), logged => logged.Id.Id == 5006);
        Assert.Contains(rejection.Fields, field => field.Key == "Reason" && Equals(field.Value, "stale_token"));
        await ReleaseAsync(newer);
    }

    [Fact]
    public async Task ATokenFromAnotherLeaseKeyIsRejected()
    {
        coordinator.Require();
        var first = await AcquireAsync("lease");
        Assert.True(await new PostgreSqlFence(coordinator).WriteAsync(Resource, first, Token));
        var other = await AcquireAsync("other");
        Assert.True(other.Token.Value > first.Token.Value);
        var seen = coordinator.Logger.Events.Count;

        Assert.False(await new PostgreSqlFence(coordinator).WriteAsync(Resource, other, Token));

        Assert.Equal(first.Token.Value, await HighestTokenAsync());
        var rejection = Assert.Single(coordinator.Logger.Events.Skip(seen), logged => logged.Id.Id == 5006);
        Assert.Contains(rejection.Fields, field => field.Key == "Reason" && Equals(field.Value, "other_lease"));
        await ReleaseAsync(first);
        await ReleaseAsync(other);
    }

    [Fact]
    public async Task AUnitInAnotherScopeFailsWithScopeRequired()
    {
        coordinator.Require();
        var lease = await AcquireAsync("lease");

        await using (var unit = await OpenAsync(coordinator.Scopes.OrganizationB))
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Guard.AcceptAsync(unit, Resource, lease, Token));
            Assert.Same(ErrorCodes.CoordinationScopeRequired, failure.Code);
            Assert.Equal(CoordinationFailureClass.ScopeViolation, CoordinationFailures.ClassOf(failure));
        }

        Assert.Null(await HighestTokenAsync());
        await ReleaseAsync(lease);
    }

    [Theory]
    [InlineData(CoordinatorKind.Local)]
    [InlineData(CoordinatorKind.Redis)]
    public async Task ALeaseFromAnotherCoordinatorFailsAndWritesNothing(CoordinatorKind issuer)
    {
        coordinator.Require();
        var foreign = Lease.Grant(Scope, new CoordinationName(_module, "lease"), new FencingToken(long.MaxValue), Guid.NewGuid(), issuer, Duration, TimeProvider.System.GetTimestamp(), TimeSpan.Zero, TimeProvider.System);

        await using (var unit = await OpenAsync(Scope))
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Guard.AcceptAsync(unit, Resource, foreign, Token));
            Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
            Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
            Assert.Equal(0, PostgreSqlUnitOfWork.From(unit).StatementsSent);
        }

        Assert.Null(await HighestTokenAsync());
    }

    [Fact]
    public async Task NoAdvisoryLockIsHeldDuringTheGuard()
    {
        coordinator.Require();
        var lease = await AcquireAsync("lease");
        await using var monitor = await coordinator.Fixture.OpenSuperuserAsync(coordinator.Database.Name);
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
            monitor);

        for (var round = 0; round < 5; round++)
        {
            await using var unit = await OpenAsync(Scope);
            await coordinator.Guard.AcceptAsync(unit, Resource, lease, Token);

            // Sampled while the unit that ran the guard is still open, holding whatever it took.
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(Token))!);
            await unit.CommitAsync(Token);
        }

        await ReleaseAsync(lease);
    }

    private async Task<Lease> AcquireAsync(string name) =>
        (await coordinator.Leases(0).AcquireAsync(Scope, new CoordinationName(_module, name), Duration, LeaseWait.TryOnly, Token)).Lease!;

    private async Task ReleaseAsync(Lease lease) => await coordinator.Leases(0).ReleaseAsync(lease, Token);

    private ValueTask<IUnitOfWork> OpenAsync(CoordinationScope scope) =>
        coordinator.Units(1).OpenAsync(scope.UnitScope, new UnitOfWorkOptions("scoped", "guard.test"), Token);

    // Read as the superuser, past row-level security, from outside any unit under test.
    private async Task<long?> HighestTokenAsync()
    {
        var resourceHash = CoordinationKey.Hash(Scope.Owner, Resource.Module, Resource.Name);
        await using var connection = await coordinator.Fixture.OpenSuperuserAsync(coordinator.Database.Name);
        await using var command = new NpgsqlCommand("SELECT max(highest_token) FROM isk_coordination.fences WHERE resource_hash = $1", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = resourceHash });
        return await command.ExecuteScalarAsync(Token) is long value ? value : null;
    }
}

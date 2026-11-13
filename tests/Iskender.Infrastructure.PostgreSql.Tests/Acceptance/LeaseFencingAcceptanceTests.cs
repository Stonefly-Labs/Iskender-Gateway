using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Scoped;

namespace Iskender.Infrastructure.PostgreSql.Tests.Acceptance;

/// <summary>
/// User Story 2 against PostgreSQL leases and <c>PostgreSqlFencingGuard</c>: scenarios 1 to 4, and the pause-and-expire
/// sequence with two independent clients, each on a data source of its own.
/// </summary>
public class LeaseFencingAcceptanceTests(PostgreSqlCoordinatorUnderTest coordinator)
    : Iskender.Coordination.Conformance.Acceptance.LeaseFencingAcceptanceTests, IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private const string ProtectedWriteSql = "INSERT INTO isk_scoped.slots (scope_key, name, value) VALUES ($1, $2, $3)";

    protected override ICoordinatorUnderTest Coordinator => coordinator;

    protected override IFenceUnderTest Fence { get; } = new PostgreSqlFence(coordinator);

    [Fact]
    public async Task APausedHoldersWriteAfterExpiryIsRejectedAndItsUnitCommitsNothing()
    {
        coordinator.Require();
        var (a, b) = (coordinator.Leases(0), coordinator.Leases(1));

        // A acquires, then pauses: its renewal is withheld until the lease has expired on the server.
        var paused = (await a.AcquireAsync(Scope, Name, ShortLease, LeaseWait.TryOnly, Token)).Lease!;
        await coordinator.PassTimeAsync(ShortLease + Slack);

        // B acquires and writes through the guard.
        var current = (await b.AcquireAsync(Scope, Name, LongLease, LeaseWait.TryOnly, Token)).Lease!;
        Assert.True(current.Token.Value > paused.Token.Value);
        await using (var unit = await coordinator.Units(1).OpenAsync(Scope.UnitScope, new UnitOfWorkOptions("scoped", "acceptance.write"), Token))
        {
            await coordinator.Guard.AcceptAsync(unit, Resource, current, Token);
            await WriteProtectedAsync(unit, "b", current);
            await unit.CommitAsync(Token);
        }

        // A wakes and writes: its protected row first, then the guard, which faults the unit and so rolls the row back.
        await using (var unit = await coordinator.Units(0).OpenAsync(Scope.UnitScope, new UnitOfWorkOptions("scoped", "acceptance.write"), Token))
        {
            await WriteProtectedAsync(unit, "a", paused);
            var rejected = await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Guard.AcceptAsync(unit, Resource, paused, Token));
            Assert.Same(ErrorCodes.CoordinationFencingRejected, rejected.Code);
            Assert.Equal(UnitOfWorkState.Faulted, unit.State);
            await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token));
        }

        // Read from a third, unrelated unit: only B's row exists.
        await using (var unit = await coordinator.Units(2).OpenAsync(Scope.UnitScope, new UnitOfWorkOptions("scoped", "acceptance.read"), Token))
        {
            var rows = await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, "SELECT name, value FROM isk_scoped.slots WHERE {scope} AND left(name, length($2)) = $2 ORDER BY name")
                .With(Prefix())
                .QueryAsync(reader => (Name: reader.GetString(0), Value: reader.GetInt64(1)), Token);
            Assert.Equal([(Prefix() + "b", current.Token.Value)], rows);
        }

        await b.ReleaseAsync(current, Token);
    }

    private string Prefix() => PostgreSqlFence.Protected(Resource) + "!";

    private async Task WriteProtectedAsync(IUnitOfWork unit, string writer, Lease lease) =>
        await PostgreSqlUnitOfWork.From(unit).Command(ScopedTables.Slots, ProtectedWriteSql)
            .With(Prefix() + writer).With(lease.Token.Value)
            .ExecuteAsync(Token);
}

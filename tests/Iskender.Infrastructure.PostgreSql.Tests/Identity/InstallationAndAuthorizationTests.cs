using System.Net;
using System.Net.Sockets;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Tests.Identity;

/// <summary>
/// The installation state and authorization stores (ISK-0010 T011, research R3 and R10): no row is uninitialized, a row is
/// initialized with its time, a grant decides on every call with no cache, and an unreachable store throws
/// <c>persistence.unavailable</c> from both (FR-001, FR-015).
/// </summary>
public class InstallationAndAuthorizationTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(ScratchDatabase Database, TestStore Store)> MigratedAsync()
    {
        fixture.RequirePostgreSql();
        var database = await ScratchDatabase.CreateAsync(fixture);
        var store = TestStore.For(database);
        await store.Runner(new MigrationSet([DirectoryMigrations.Module, WorkMigrations.Module, AuditMigrations.Module, IdentityMigrations.Module]))
            .ApplyAsync(new MigrationApplyRequest(), Token);
        return (database, store);
    }

    [Fact]
    public async Task AnEmptyDatabaseIsUninitializedAndARowIsInitializedWithItsTime()
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;
        var state = new PostgreSqlInstallationState(store.Factory);

        Assert.Same(InstallationStatus.Uninitialized, await state.ReadAsync(Token));

        await fixture.ExecuteAsync(database.Name, "INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', timestamptz '2026-11-21 10:00:00+00', 'prin_a')");

        var initialized = await state.ReadAsync(Token);
        Assert.True(initialized.IsInitialized);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero), initialized.InitializedAt);
    }

    [Fact]
    public async Task OnlyAGrantedPrincipalIsAllowedAndAGrantTakesEffectOnTheNextCall()
    {
        var (database, store) = await MigratedAsync();
        await using var _ = database;
        await using var __ = store;
        var authorization = new PostgreSqlOperatorAuthorization(store.Factory);
        var unknown = ResourceId.New(ResourceTypes.Principal);
        var ungranted = ResourceId.New(ResourceTypes.Principal);
        var granted = ResourceId.New(ResourceTypes.Principal);
        await fixture.ExecuteAsync(database.Name,
            $"INSERT INTO isk_identity.principals VALUES ('{ungranted.Value}', 'operator', now(), 'bootstrap'), ('{granted.Value}', 'operator', now(), 'recovery'); " +
            $"INSERT INTO isk_identity.operator_grants VALUES ('{granted.Value}', 'system_operator', now())");

        Assert.Equal(OperatorDecision.Denied, await authorization.DecideAsync(unknown, OperatorActions.InstallationRead, Token));
        Assert.Equal(OperatorDecision.Denied, await authorization.DecideAsync(ungranted, OperatorActions.InstallationRead, Token));
        Assert.Equal(OperatorDecision.Allowed, await authorization.DecideAsync(granted, OperatorActions.InstallationRead, Token));
        Assert.Equal(OperatorDecision.Denied, await authorization.DecideAsync(granted, new OperatorAction("installation.write"), Token));
        Assert.Equal(OperatorDecision.Denied, await authorization.DecideAsync(ResourceId.New(ResourceTypes.Project), OperatorActions.InstallationRead, Token));

        await fixture.ExecuteAsync(database.Name, $"INSERT INTO isk_identity.operator_grants VALUES ('{ungranted.Value}', 'system_operator', now())");
        Assert.Equal(OperatorDecision.Allowed, await authorization.DecideAsync(ungranted, OperatorActions.InstallationRead, Token));

        await fixture.ExecuteAsync(database.Name, $"DELETE FROM isk_identity.operator_grants WHERE principal_id = '{granted.Value}'");
        Assert.Equal(OperatorDecision.Denied, await authorization.DecideAsync(granted, OperatorActions.InstallationRead, Token));
    }

    [Fact]
    public async Task AnUnreachableStoreThrowsPersistenceUnavailableFromBoth()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["port"] = ClosedPort() });

        var state = await Assert.ThrowsAsync<GatewayException>(async () => await new PostgreSqlInstallationState(store.Factory).ReadAsync(Token));
        var decision = await Assert.ThrowsAsync<GatewayException>(async () =>
            await new PostgreSqlOperatorAuthorization(store.Factory).DecideAsync(ResourceId.New(ResourceTypes.Principal), OperatorActions.InstallationRead, Token));

        Assert.Same(ErrorCodes.PersistenceUnavailable, state.Code);
        Assert.Same(ErrorCodes.PersistenceUnavailable, decision.Code);
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceDiagnostics = Iskender.Coordination.Conformance.CapturingDiagnostics;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// PostgreSQL's health probe (FR-025, SC-006, research R17): <c>ok</c>, <c>unreachable</c> with the server stopped, and
/// <c>authentication_failed</c> with a failing credential provider, each within <c>healthProbeTimeout</c> plus the fixed
/// 2-second allowance, and the caller's token giving <c>coordination.cancelled</c>.
/// </summary>
public class CoordinationHealthTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    // The contract's fixed safety bound past healthProbeTimeout.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Iskender.Infrastructure.Coordination.CoordinationSettings Settings { get; } =
        PostgreSqlCoordinatorUnderTest.ReadSettings("\"healthProbeTimeout\":\"1s\"");

    [Fact]
    public async Task AnAnsweringServerIsOk()
    {
        coordinator.Require();
        var probe = new PostgreSqlCoordinationHealth(coordinator.Units(0), Settings, new ConformanceDiagnostics.Logger());

        Assert.Equal(CoordinationHealthResult.Healthy, await CheckWithinBoundAsync(probe));
    }

    [Fact]
    public async Task AFailingCredentialProviderIsAuthenticationFailed()
    {
        coordinator.Require();
        await using var store = TestStore.For(coordinator.Database);
        store.Credentials.Failure = new InvalidOperationException("no secret");
        var probe = new PostgreSqlCoordinationHealth(store.Factory, Settings, new ConformanceDiagnostics.Logger());

        Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.AuthenticationFailed), await CheckWithinBoundAsync(probe));
    }

    [Fact]
    public async Task AStoppedServerIsUnreachable()
    {
        coordinator.Require();

        // A container of its own, so stopping it leaves every other class's database alone.
        await using var fixture = new PostgreSqlFixture();
        await fixture.InitializeAsync();
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        await using var store = TestStore.For(database);
        var probe = new PostgreSqlCoordinationHealth(store.Factory, Settings, new ConformanceDiagnostics.Logger());
        Assert.Equal(CoordinationHealthResult.Healthy, await probe.CheckAsync(Token));

        await fixture.StopAsync();
        try
        {
            Assert.Equal(CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Unreachable), await CheckWithinBoundAsync(probe));
        }
        finally
        {
            // Started again, so the scratch database can be dropped before the container is removed.
            await fixture.RestartAsync();
        }
    }

    [Fact]
    public async Task TheCallersTokenGivesCancelled()
    {
        coordinator.Require();
        var probe = new PostgreSqlCoordinationHealth(coordinator.Units(0), Settings);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await caller.CancelAsync();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await probe.CheckAsync(caller.Token));

        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
    }

    private static async Task<CoordinationHealthResult> CheckWithinBoundAsync(PostgreSqlCoordinationHealth probe)
    {
        var watch = Stopwatch.StartNew();
        var result = await probe.CheckAsync(Token);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, Settings.HealthProbeTimeout + Allowance);
        return result;
    }
}

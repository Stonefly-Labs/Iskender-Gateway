using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// User Story 3 scenario 4 and the unknown-outcome edge case against PostgreSQL (FR-022): an outage is classified within
/// the operation bound and never reported as granted, and a commit whose outcome is unknown says so.
/// </summary>
public class OutageClassificationTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    // Covers scheduling and the driver noticing a closed socket; well under any wait a retry would add.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly CoordinationName _name = new("outage", "counter_" + Guid.NewGuid().ToString("N")[..8]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario4AStoppedServerFailsAReservationWithUnavailableWithinTheBound()
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
        var atomic = new PostgreSqlAtomicOperations(store.Factory, settings);
        var scope = new ConformanceScopes().OrganizationA;
        Assert.True((await atomic.ReserveAsync(scope, _name, 1, 100, Expiry, Token)).Granted);

        await fixture.StopAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(scope, _name, 1, 100, Expiry, Token));
            Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
            Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(failure));
            Assert.InRange(watch.Elapsed, TimeSpan.Zero, settings.OperationTimeout + Allowance);
        }
        finally
        {
            // Started again, so the scratch database can be dropped before the container is removed.
            await fixture.RestartAsync();
        }
    }

    [Fact]
    public async Task AReservationWhoseCommitOutcomeIsUnknownSaysSo()
    {
        coordinator.Require();
        var atomic = new PostgreSqlAtomicOperations(new BrokenCommits(coordinator.Units(0), coordinator.Fixture), coordinator.CoordinationSettings);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(coordinator.Scopes.OrganizationA, _name, 1, 100, Expiry, Token));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(failure));
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));
        Assert.Null(failure.InnerException);
    }

    /// <summary>Opens real units whose connection is terminated during <c>COMMIT</c>, as a network failure mid-commit would.</summary>
    private sealed class BrokenCommits(PostgreSqlUnitOfWorkFactory inner, PostgreSqlFixture fixture) : IUnitOfWorkFactory
    {
        public async ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken)
        {
            var unit = await inner.OpenAsync(scope, options, cancellationToken);
            var adapter = PostgreSqlUnitOfWork.From(unit);
            var pid = await adapter.FrameworkCommand("SELECT pg_backend_pid()").ScalarAsync<int>(cancellationToken);
            adapter.DuringCommit = async () =>
            {
                await fixture.ScalarAsync<bool>("postgres", $"SELECT pg_terminate_backend({pid})");
                throw new NpgsqlException("Exception while writing to stream", new IOException("connection broken"));
            };
            return unit;
        }
    }
}

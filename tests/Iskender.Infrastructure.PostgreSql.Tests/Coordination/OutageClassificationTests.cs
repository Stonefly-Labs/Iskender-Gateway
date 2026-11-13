using System.Diagnostics;
using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// User Story 3 scenario 4 and the unknown-outcome edge case against PostgreSQL (FR-022): an outage is classified within
/// the operation bound and never reported as granted, and a commit whose outcome is unknown says so. Each writes its
/// contract Diagnostics event: 5003 for a connection failure, with its class, 5004 for an unknown outcome, and 5010 for an
/// unexpected failure (TR-16, FR-028).
/// </summary>
public class OutageClassificationTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    // Covers scheduling and the driver noticing a closed socket; well under any wait a retry would add.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly CoordinationName _name = new("outage", "counter_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly Fixture.CapturingDiagnostics.Logger _logger = new();

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
        var atomic = new PostgreSqlAtomicOperations(store.Factory, settings, _logger);
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
            AssertConnectionFailed("unavailable");
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
        var atomic = new PostgreSqlAtomicOperations(new BrokenCommits(coordinator.Units(0), coordinator.Fixture), coordinator.CoordinationSettings, _logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(coordinator.Scopes.OrganizationA, _name, 1, 100, Expiry, Token));

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(failure));
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));
        Assert.Null(failure.InnerException);
        var unknown = Assert.Single(_logger.Events, logged => logged.Id.Id == 5004);
        Assert.Equal(LogLevel.Error, unknown.Level);
        Assert.Contains(new KeyValuePair<string, object?>("Module", "outage"), unknown.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("Primitive", "atomic_operations"), unknown.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("Operation", "atomic.reserve"), unknown.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("CoordinatorKind", "postgresql"), unknown.Fields);
    }

    [Fact]
    public async Task AFailingCredentialProviderKeepsItsAuthenticationFailedClass()
    {
        coordinator.Require();
        await using var store = TestStore.For(coordinator.Database);
        store.Credentials.Failure = new InvalidOperationException("no secret");
        var atomic = new PostgreSqlAtomicOperations(store.Factory, coordinator.CoordinationSettings, _logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(coordinator.Scopes.OrganizationA, _name, 1, 100, Expiry, Token));

        // The unit never opened, so the class comes from the factory's own classification, not from a unit.
        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(failure));
        Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));
        AssertConnectionFailed("authentication_failed");
        Assert.DoesNotContain(_logger.Events, logged => logged.Id.Id == 5004);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsInternalWithEvent5010AndItsClassOnly()
    {
        var leases = new PostgreSqlLeaseCoordinator(new FailingOpens(), PostgreSqlCoordinatorUnderTest.ReadSettings(""), _logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await leases.AcquireAsync(new ConformanceScopes().OrganizationA, _name, TimeSpan.FromSeconds(30), LeaseWait.TryOnly, Token));

        Assert.Same(ErrorCodes.Internal, failure.Code);
        Assert.Equal(CoordinationFailureClass.Unexpected, CoordinationFailures.ClassOf(failure));
        var unexpected = Assert.Single(_logger.Events, logged => logged.Id.Id == 5010);
        Assert.Equal(LogLevel.Error, unexpected.Level);
        Assert.Contains(new KeyValuePair<string, object?>("Module", "outage"), unexpected.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("Operation", "leases.acquire"), unexpected.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("FailureClass", "unexpected"), unexpected.Fields);
        Assert.DoesNotContain(_logger.Events, logged => logged.Id.Id is 5003 or 5004);
    }

    private void AssertConnectionFailed(string failureClass)
    {
        var failed = Assert.Single(_logger.Events, logged => logged.Id.Id == 5003);
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Contains(new KeyValuePair<string, object?>("CoordinatorKind", "postgresql"), failed.Fields);
        Assert.Contains(new KeyValuePair<string, object?>("FailureClass", failureClass), failed.Fields);
    }

    /// <summary>A factory whose units fail to open with a code no coordination failure maps from.</summary>
    private sealed class FailingOpens : IUnitOfWorkFactory
    {
        public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken) =>
            throw new GatewayException(ErrorCodes.Internal);
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

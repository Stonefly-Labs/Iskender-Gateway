using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using ConformanceScopes = Iskender.Coordination.Conformance.ConformanceScopes;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// Spec Edge Cases and FR-015: an acquire whose caller cancels while its <c>COMMIT</c> is in flight, so that the client
/// gives up but the server still commits, leaves no lease held afterwards.
/// </summary>
public class LeaseCommitOutcomeTests(PostgreSqlFixture fixture)
{
    // Sleeps through COMMIT, ignoring the client's cancel request, so the client gives up and the server still commits.
    // It runs only while slow_commit_on holds a row, so the second client's acquire commits at once.
    private const string SlowCommitSql = """
        CREATE TABLE public.slow_commit_on (on_flag int);
        INSERT INTO public.slow_commit_on VALUES (1);
        CREATE FUNCTION public.slow_commit() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $$
        DECLARE
          until timestamptz := clock_timestamp() + interval '5 seconds';
        BEGIN
          IF EXISTS (SELECT 1 FROM public.slow_commit_on) THEN
            WHILE clock_timestamp() < until LOOP
              BEGIN
                PERFORM pg_sleep(0.1);
              EXCEPTION WHEN query_canceled THEN
                NULL;
              END;
            END LOOP;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER slow_commit AFTER INSERT OR UPDATE ON isk_coordination.leases
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.slow_commit();
        """;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CancellationWhileAnAcquiresCommitIsInFlightLeavesNoLeaseHeld()
    {
        fixture.RequirePostgreSql();

        // A database of its own, so the slow commit leaves every other test's leases alone.
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await PostgreSqlCoordinatorUnderTest.ApplyAsync(database);
        await fixture.ExecuteAsync(database.Name, SlowCommitSql);
        var settings = PostgreSqlCoordinatorUnderTest.ReadSettings(PostgreSqlCoordinatorUnderTest.Section + ",\"operationTimeout\":\"10s\"");
        await using var first = TestStore.For(database);
        await using var second = TestStore.For(database);
        var logger = new Fixture.CapturingDiagnostics.Logger();
        var (a, b) = (new PostgreSqlLeaseCoordinator(first.Factory, settings, logger), new PostgreSqlLeaseCoordinator(second.Factory, settings));
        var scope = new ConformanceScopes().OrganizationA;
        var name = new CoordinationName("commit_outcome", "lease");

        // A lease far longer than the test, so only the release can free it.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var acquiring = a.AcquireAsync(scope, name, TimeSpan.FromSeconds(60), LeaseWait.TryOnly, cancellation.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
        await cancellation.CancelAsync();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => acquiring.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
        Assert.Equal(CoordinationFailureClass.Cancelled, CoordinationFailures.ClassOf(failure));
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));

        // The server committed the grant once the slow commit ended; only the release by holder can have removed it.
        await fixture.ExecuteAsync(database.Name, "DELETE FROM public.slow_commit_on");
        await WaitUntilNoBackendIsCommittingAsync(database);
        var other = await b.AcquireAsync(scope, name, Duration, LeaseWait.TryOnly, Token);
        Assert.True(other.IsAcquired);
        await b.ReleaseAsync(other.Lease!, Token);
    }

    // The abandoned backend finishes its commit when the slow trigger ends, within about 5 seconds.
    private async Task WaitUntilNoBackendIsCommittingAsync(ClassDatabase database)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var busy = await fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{database.Name}' AND state = 'active' AND pid <> pg_backend_pid()");
            if (busy == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }
    }
}

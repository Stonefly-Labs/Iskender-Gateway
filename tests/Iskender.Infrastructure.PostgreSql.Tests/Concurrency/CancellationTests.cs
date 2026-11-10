using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Concurrency;

/// <summary>User Story 2 scenario 7, the bounds Edge Cases, FR-009 and FR-015.</summary>
public class CancellationTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AUnitDisposedWithoutCommitLeavesNoChange()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var id = Id();

        await using (var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA)))
        {
            await WidgetRepository.InsertAsync(unit, id, "never", Token);
        }

        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task TheCallersTokenCancelsTheStatementOnTheServerAndRollsBack()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var id = Id();
        var marker = Guid.NewGuid().ToString("N");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        await WidgetRepository.InsertAsync(unit, id, "cancelled", Token);
        caller.CancelAfter(TimeSpan.FromMilliseconds(300));
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand($"SELECT pg_sleep(30) /* {marker} */").ExecuteAsync(caller.Token));

        Assert.Same(ErrorCodes.PersistenceCancelled, failure.Code);
        Assert.Equal(PersistenceFailureClass.Cancelled, unit.Failure!.FailureClass);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        await AssertNotRunningAsync(marker);
        await unit.DisposeAsync();
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task AFiredDeadlineGivesATimeout()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var marker = Guid.NewGuid().ToString("N");
        var options = TestStore.Options() with { Deadline = DateTimeOffset.UtcNow.AddMilliseconds(800) };

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA), options);
        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand($"SELECT pg_sleep(30) /* {marker} */").ExecuteAsync(Token));

        Assert.Same(ErrorCodes.PersistenceTimeout, failure.Code);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(800) + Allowance);
        await AssertNotRunningAsync(marker);
    }

    [Fact]
    public async Task ADeadlineAlreadyPassedAcquiresNoConnection()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var options = TestStore.Options() with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) };

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.OpenAsync(Owners.Organization(Owners.OrgA), options));

        Assert.Same(ErrorCodes.PersistenceTimeout, failure.Code);
        Assert.Empty(store.Credentials.Requests);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4001);
    }

    [Theory]
    [InlineData("1s", null)]
    [InlineData("30s", 1000)]
    public async Task AnExhaustedPoolWaitsNoLongerThanTheShorterBound(string connectionTimeout, int? deadlineMilliseconds)
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["poolMinimum"] = 0, ["poolMaximum"] = 1, ["connectionTimeout"] = connectionTimeout });
        await using var holder = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var options = deadlineMilliseconds is { } ms ? TestStore.Options() with { Deadline = DateTimeOffset.UtcNow.AddMilliseconds(ms) } : TestStore.Options();

        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await store.OpenAsync(Owners.Organization(Owners.OrgB), options));

        Assert.Same(ErrorCodes.PersistenceTimeout, failure.Code);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1) + Allowance);
        Assert.Equal(UnitOfWorkState.Open, holder.State);
    }

    [Fact]
    public async Task StatementTimeoutGivesATimeout()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database, new Dictionary<string, object> { ["statementTimeout"] = "1s" });

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        var clock = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT pg_sleep(30)").ExecuteAsync(Token));

        Assert.Same(ErrorCodes.PersistenceTimeout, failure.Code);
        Assert.Equal(PersistenceFailureClass.Timeout, unit.Failure!.FailureClass);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1) + Allowance);
    }

    [Fact]
    public async Task AConnectionBrokenDuringCommitIsAnUnknownOutcomeThatIsNeverRetried()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var id = Id();
        var runs = 0;

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await UnitOfWorkRetry.RunAsync(
            store.Factory,
            Owners.Organization(Owners.OrgA),
            TestStore.Options() with { SafeToRerun = true },
            async (unit, token) =>
            {
                runs++;
                var adapter = PostgreSqlUnitOfWork.From(unit);
                await WidgetRepository.InsertAsync(unit, id, "unknown", token);
                var pid = await adapter.FrameworkCommand("SELECT pg_backend_pid()").ScalarAsync<int>(token);
                adapter.DuringCommit = async () =>
                {
                    // Break the connection for real, then report what Npgsql reports for a socket failure mid-COMMIT.
                    await database.Fixture.ScalarAsync<bool>("postgres", $"SELECT pg_terminate_backend({pid})");
                    throw new NpgsqlException("Exception while writing to stream", new IOException("connection broken"));
                };
                return true;
            },
            Token,
            logger: store.Logger));

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(1, runs);
        Assert.Contains(store.Logger.Events, logged => logged.Id.Id == 4004);
        Assert.DoesNotContain(store.Logger.Events, logged => logged.Id.Id == 4005);
        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task TheUnitRecordsAnUnknownCommitOutcome()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);

        await using var unit = await store.OpenAsync(Owners.Organization(Owners.OrgA));
        PostgreSqlUnitOfWork.From(unit).DuringCommit = () => throw new NpgsqlException("Exception while reading from stream", new IOException("connection broken"));

        Assert.Same(ErrorCodes.PersistenceUnavailable, (await Assert.ThrowsAsync<GatewayException>(async () => await unit.CommitAsync(Token))).Code);
        Assert.Equal(UnitOfWorkState.Faulted, unit.State);
        Assert.True(unit.Failure!.CommitOutcomeUnknown);
        Assert.Equal(PersistenceFailureClass.Unavailable, unit.Failure.FailureClass);
    }

    private static string Id() => $"w-cancel-{Guid.NewGuid():N}";

    private async Task<long> CountAsync(string id) =>
        await database.Fixture.ScalarAsync<long>(database.Name, $"SELECT count(*) FROM isk_alpha.widgets WHERE id = '{id}'");

    // The server stopped the statement: it no longer appears in pg_stat_activity.
    private async Task AssertNotRunningAsync(string marker)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await database.Fixture.ScalarAsync<long>("postgres", $"SELECT count(*) FROM pg_stat_activity WHERE state = 'active' AND query LIKE '%pg_sleep(30) /* {marker} */%' AND pid <> pg_backend_pid()") == 0)
            {
                return;
            }

            await Task.Delay(20, Token);
        }

        Assert.Fail("The cancelled statement was still running on the server.");
    }
}

using System.Net;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Concurrency;

/// <summary>
/// Exactly one bootstrap succeeds (ISK-0010 T026, US2.3, SC-002, FR-009, research R5, plan Risks): 20 runs of 50
/// concurrent redemptions of one token across two hosts each end with one success, 49 uniform failures, and one
/// principal; and a redemption held between its statements while a new token is issued leaves at most one principal and
/// one installation row. The database is reset between runs; waits poll a state with a bound, and nothing sleeps for a
/// fixed time.
/// </summary>
public class ConcurrentRedemptionTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const int Runs = 20;
    private const int Redemptions = 50;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (operators.Postgres.UnavailableReason is null)
        {
            await operators.ResetAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task FiftyConcurrentRedemptionsAcrossTwoHostsSucceedExactlyOnceInEveryRun()
    {
        operators.RequirePostgreSql();
        var options = new OperatorHostOptions { OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":600}" };
        await using var first = await operators.HostAsync(options);
        await using var second = await operators.HostAsync(options);

        for (var run = 0; run < Runs; run++)
        {
            await operators.ResetAsync();
            var token = (await IssueAsync()).Reveal();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, Redemptions).Select(index => Task.Run(async () =>
            {
                await start.Task;
                using var response = await BootstrapRedemptionTests.RedeemAsync(index % 2 == 0 ? first : second, "/admin/v1/bootstrap", token);
                return response.StatusCode;
            })).ToList();
            start.SetResult();

            var statuses = await Task.WhenAll(attempts);

            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
            Assert.Equal(Redemptions - 1, statuses.Count(status => status == HttpStatusCode.Forbidden));
            Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
            Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
            Assert.Equal(Redemptions - 1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'consumed'"));
        }
    }

    public static TheoryData<string> Steps() => ["locked", "installed", "written"];

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task ARedemptionHeldBetweenItsStatementsWhileANewTokenIsIssuedLeavesOnePrincipal(string step)
    {
        operators.RequirePostgreSql();
        for (var run = 0; run < Runs; run++)
        {
            await operators.ResetAsync();
            var held = (await IssueAsync()).Reveal();
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var redemption = new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes, async (reached, cancellationToken) =>
            {
                if (reached == step)
                {
                    paused.SetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
            });

            var redeeming = redemption.RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(held), OperatorFixture.Context(), Token).AsTask();
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            var issuing = IssueAsync();

            // The issue's supersede waits on the held redemption's row lock, so release once it is waiting.
            await WaitUntilAsync(async () => issuing.IsCompleted || await operators.ScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()") > 0);
            release.SetResult();

            var outcome = await redeeming;
            Assert.True(outcome.Succeeded);

            // The issue waited on the redemption's row lock, so it reads the committed installation and is refused:
            // no token is printed, and no token row or issue record is written (FR-003, US2.5, Tokens rule 6).
            var refused = await Assert.ThrowsAsync<Core.Contracts.Errors.GatewayException>(() => issuing);
            Assert.Same(Core.Contracts.Errors.ErrorCodes.OperatorAlreadyInitialized, refused.Code);
            Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE purpose = 'bootstrap'"));
            Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap_token.issued'"));
            Assert.Equal(0L, await operators.AuditCountAsync("action = 'token.superseded'"));

            Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
            Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 300 && !await condition(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        Assert.True(await condition());
    }

    private Task<IssuedOperatorToken> IssueAsync() =>
        new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token).AsTask();
}

using System.Net;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Bootstrap;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management.Bootstrap;

namespace Iskender.Identity.Tests.Recovery;

/// <summary>
/// The recovery draft (ISK-0010 T044, US6.1 to US6.4, FR-023, FR-024): on an initialized installation a recovery token
/// redeemed at the recovery route creates a second principal with <c>system_operator</c> and its enrollment code and leaves
/// the first unchanged; <c>bootstrap recover</c> on an uninitialized installation exits 9 and issues nothing; a redeemed
/// recovery token, a token at the other purpose's route, and a recovery token on an uninitialized installation each get
/// the uniform failure, with reasons <c>consumed</c>, <c>wrong_purpose</c>, and <c>not_initialized</c>.
/// </summary>
public class RecoveryTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
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
    public async Task ARecoveryRedemptionAddsASecondOperatorAndLeavesTheFirstUnchanged()
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync();
        var first = await PrincipalAsync(await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/bootstrap", await IssueAsync(OperatorTokenPurpose.Bootstrap)));
        var firstRows = await FirstRowsAsync(first);
        var recovery = await IssueAsync(OperatorTokenPurpose.Recovery);

        var second = await PrincipalAsync(await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/recovery", recovery));

        Assert.NotEqual(first, second);
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.principals WHERE principal_id = '{second}' AND created_through = 'recovery'"));
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.operator_grants WHERE principal_id = '{second}' AND role = 'system_operator'"));
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE purpose = 'enrollment' AND principal_id = '{second}'"));
        Assert.Equal(firstRows, await FirstRowsAsync(first));
        Assert.Equal(first, await operators.ScalarAsync<string>("SELECT initialized_by FROM isk_identity.installation"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery_token.issued'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed'"));
        Assert.Equal(1L, await operators.AuditCountAsync($"action = 'recovery.redeemed' AND result = 'succeeded' AND target_principal = '{second}'"));

        using var again = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/recovery", recovery);

        await AdminRouteTests.AssertErrorAsync(again, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'consumed'"));
        Assert.Equal(2L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
    }

    [Fact]
    public async Task RecoverOnAnUninitializedInstallationExits9AndIssuesNothing()
    {
        operators.RequirePostgreSql();
        var run = new BootstrapCommandTests.Run(operators);

        var outcome = await run.CommandAsync(BootstrapOperation.Recover);

        Assert.Equal((9, ErrorCodes.OperatorNotInitialized.Id), (outcome.ExitCode, outcome.Code?.Id));
        Assert.Equal("", run.StandardOutput.ToString());
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens"));
        Assert.Equal(0L, await operators.AuditCountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATokenAtTheOtherPurposesRouteFailsAsWrongPurpose(bool initialized)
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync();
        var bootstrap = await IssueAsync(OperatorTokenPurpose.Bootstrap);
        await operators.InitializeInstallationAsync();
        var recovery = await IssueAsync(OperatorTokenPurpose.Recovery);
        if (!initialized)
        {
            await operators.ExecuteAsync("DELETE FROM isk_identity.installation");
        }

        using var atRecovery = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/recovery", bootstrap);
        using var atBootstrap = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/bootstrap", recovery);

        await AdminRouteTests.AssertErrorAsync(atRecovery, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        await AdminRouteTests.AssertErrorAsync(atBootstrap, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'wrong_purpose'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'wrong_purpose'"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal(initialized ? 1L : 0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
    }

    [Fact]
    public async Task ARecoveryTokenOnAnUninitializedInstallationFailsAsNotInitialized()
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync();
        await operators.InitializeInstallationAsync();
        var recovery = await IssueAsync(OperatorTokenPurpose.Recovery);
        await operators.ExecuteAsync("DELETE FROM isk_identity.installation");

        using var response = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/recovery", recovery);

        await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'not_initialized'"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE consumed_at IS NOT NULL"));
    }

    private async Task<string> IssueAsync(OperatorTokenPurpose purpose) =>
        (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(purpose, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();

    private Task<string> FirstRowsAsync(string principal) =>
        operators.ScalarAsync<string>(
            $"SELECT (SELECT p::text FROM isk_identity.principals p WHERE principal_id = '{principal}') || ' ' || (SELECT string_agg(g::text, ',') FROM isk_identity.operator_grants g WHERE principal_id = '{principal}')");

    private static async Task<string> PrincipalAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
            return body.RootElement.GetProperty("principalId").GetString()!;
        }
    }
}

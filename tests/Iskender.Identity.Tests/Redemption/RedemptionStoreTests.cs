using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Redemption;

/// <summary>
/// The redemption store through the store-level fixture (ISK-0010 T023, research R5, operator-security contract Redemption
/// rules 1 to 3 and 6, Enrollment rule 1, FR-008 to FR-011, FR-023): each branch, with the rows it leaves.
/// </summary>
public class RedemptionStoreTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task ABootstrapInitializesWithOnePrincipalGrantEnrollmentCodeAndRecord()
    {
        operators.RequirePostgreSql();
        var issued = await IssueAsync(OperatorTokenPurpose.Bootstrap);

        var outcome = await Redemption().RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(issued.Reveal()), OperatorFixture.Context(), Token);

        Assert.True(outcome.Succeeded);
        var principal = outcome.Principal.Value;
        Assert.Equal($"initialized|{principal}", await operators.ScalarAsync<string>("SELECT state || '|' || initialized_by FROM isk_identity.installation"));
        Assert.Equal($"{principal}|operator|bootstrap", await operators.ScalarAsync<string>("SELECT string_agg(principal_id || '|' || kind || '|' || created_through, ',') FROM isk_identity.principals"));
        Assert.Equal($"{principal}|system_operator", await operators.ScalarAsync<string>("SELECT string_agg(principal_id || '|' || role, ',') FROM isk_identity.operator_grants"));
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE token_id = '{issued.TokenId}' AND consumed_at IS NOT NULL"));

        var code = outcome.EnrollmentCode;
        Assert.Equal(OperatorTokenPurpose.Enrollment, code.Purpose);
        Assert.Equal(
            $"{principal}|86400",
            await operators.ScalarAsync<string>($"SELECT principal_id || '|' || extract(epoch FROM expires_at - issued_at)::bigint FROM isk_identity.tokens WHERE token_id = '{code.TokenId}' AND purpose = 'enrollment' AND consumed_at IS NULL"));
        var text = code.Reveal();
        Assert.StartsWith("iske_", text, StringComparison.Ordinal);
        Assert.DoesNotContain(OperatorFixture.Secret(text), await operators.AllRowsAsync(), StringComparison.Ordinal);
        Assert.Equal(
            $"anonymous|{principal}|bootstrap.redeemed|succeeded|none",
            await operators.ScalarAsync<string>("SELECT actor || '|' || target_principal || '|' || action || '|' || result || '|' || reason FROM isk_audit.records WHERE action = 'bootstrap.redeemed'"));
    }

    [Fact]
    public async Task ARecoveryAddsANewPrincipalAndLeavesTheOthersAlone()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var existing = await operators.PrincipalAsync();
        var before = await operators.ScalarAsync<string>("SELECT string_agg(p.principal_id || g.role, ',') FROM isk_identity.principals p JOIN isk_identity.operator_grants g USING (principal_id)");
        var issued = await IssueAsync(OperatorTokenPurpose.Recovery);

        var outcome = await Redemption().RedeemAsync(OperatorTokenPurpose.Recovery, OperatorFixture.Parse(issued.Reveal()), OperatorFixture.Context(), Token);

        Assert.True(outcome.Succeeded);
        Assert.NotEqual(existing, outcome.Principal);
        Assert.Equal(2L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal("recovery", await operators.ScalarAsync<string>($"SELECT created_through FROM isk_identity.principals WHERE principal_id = '{outcome.Principal.Value}'"));
        Assert.Contains(before, await operators.ScalarAsync<string>("SELECT string_agg(p.principal_id || g.role, ',') FROM isk_identity.principals p JOIN isk_identity.operator_grants g USING (principal_id)"), StringComparison.Ordinal);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'succeeded'"));
    }

    [Fact]
    public async Task AnUnknownIdentifierRollsBackWithNoRecord()
    {
        operators.RequirePostgreSql();
        var never = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal();
        var before = await operators.AllRowsAsync();

        var outcome = await Redemption().RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(never), OperatorFixture.Context(), Token);

        Assert.Equal(RedemptionFailureReason.Unknown, outcome.FailureReason);
        Assert.Equal(before, await operators.AllRowsAsync());
    }

    public static TheoryData<string> Refusals() => ["wrong_secret", "wrong_purpose", "consumed", "superseded", "expired", "already_initialized", "not_initialized"];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EachRefusalWritesOneFailureRecordAndChangesNoToken(string reason)
    {
        operators.RequirePostgreSql();
        var (route, text) = await ArrangeAsync(reason);
        var tokens = await operators.RowsAsync("isk_identity.tokens");
        var records = await operators.AuditCountAsync();

        var outcome = await Redemption().RedeemAsync(route, OperatorFixture.Parse(text), OperatorFixture.Context(), Token);

        Assert.Equal(reason, outcome.FailureReason?.ToCode());
        Assert.Equal(tokens, await operators.RowsAsync("isk_identity.tokens"));
        Assert.Equal(records + 1, await operators.AuditCountAsync());
        Assert.Equal(
            $"anonymous|{route.ToCode()}.redeemed|failed|{reason}|null",
            await operators.ScalarAsync<string>("SELECT actor || '|' || action || '|' || result || '|' || reason || '|' || coalesce(target_principal, 'null') FROM isk_audit.records ORDER BY recorded_at DESC, audit_id LIMIT 1"));
        Assert.Equal(reason == "consumed" ? 1L : 0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
    }

    // The prefix is informational and the stored purpose decides (Tokens rule 1): a genuine bootstrap token re-prefixed as
    // a recovery token verifies under its stored purpose, so it fails at the recovery route on its purpose, not its secret
    // (research R4 and R5; CONV-0010-006).
    [Fact]
    public async Task AGenuineBootstrapTokenReprefixedForRecoveryFailsWithWrongPurpose()
    {
        operators.RequirePostgreSql();
        var text = OperatorFixture.Reprefix((await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal(), 'r');
        var tokens = await operators.RowsAsync("isk_identity.tokens");

        var outcome = await Redemption().RedeemAsync(OperatorTokenPurpose.Recovery, OperatorFixture.Parse(text), OperatorFixture.Context(), Token);

        Assert.Equal(RedemptionFailureReason.WrongPurpose, outcome.FailureReason);
        Assert.Equal(tokens, await operators.RowsAsync("isk_identity.tokens"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'wrong_purpose'"));
    }

    // And a genuine bootstrap token presented with another prefix at its own route still redeems.
    [Fact]
    public async Task AGenuineBootstrapTokenWithAnotherPrefixStillRedeemsAtItsOwnRoute()
    {
        operators.RequirePostgreSql();
        var text = OperatorFixture.Reprefix((await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal(), 'e');

        var outcome = await Redemption().RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(text), OperatorFixture.Context(), Token);

        Assert.True(outcome.Succeeded);
    }

    // The redemption holds the row lock across the expiry, between its check and its consumption: the consumption judges
    // expiry by its own clock, so the token is not consumed, the installation row rolls back, and the failure is recorded
    // as expired (spec Edge Cases, FR-005; CONV-0010-007).
    [Fact]
    public async Task ATokenThatExpiresWhileItsRedemptionHoldsTheLockIsNotConsumed()
    {
        operators.RequirePostgreSql();
        var lifetimes = new IdentityLifetimes(TimeSpan.FromSeconds(10), OperatorFixture.Configuration().Lifetimes.EnrollmentCodeLifetime);
        var issued = await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redemption = new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes, async (step, cancellationToken) =>
        {
            if (step == "installed")
            {
                paused.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        });

        var redeeming = redemption.RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(issued.Reveal()), OperatorFixture.Context(), Token).AsTask();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // The check passed before the expiry; wait, by the database's clock, until it has passed.
        var expiry = $"SELECT clock_timestamp() > expires_at FROM isk_identity.tokens WHERE token_id = '{issued.TokenId}'";
        Assert.False(await operators.ScalarAsync<bool>(expiry));
        for (var attempt = 0; attempt < 300 && !await operators.ScalarAsync<bool>(expiry); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        release.SetResult();
        var outcome = await redeeming;

        Assert.Equal(RedemptionFailureReason.Expired, outcome.FailureReason);
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE consumed_at IS NOT NULL"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'expired'"));
    }

    [Fact]
    public async Task AnEnrollmentRouteOrNullArgumentFailsBeforeAnyStatement()
    {
        operators.RequirePostgreSql();
        var redemption = new PostgreSqlOperatorRedemption(operators.UnreachableFactory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes);
        var token = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await redemption.RedeemAsync(OperatorTokenPurpose.Enrollment, token, OperatorFixture.Context(), Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await redemption.RedeemAsync(OperatorTokenPurpose.Bootstrap, null!, OperatorFixture.Context(), Token))).Code);
    }

    [Fact]
    public async Task AnUnreachableStoreThrowsThePersistenceFailure()
    {
        operators.RequirePostgreSql();
        var redemption = new PostgreSqlOperatorRedemption(operators.UnreachableFactory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await redemption.RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), OperatorFixture.Context(), Token));

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
    }

    // Each refusal's state, and the route and text to redeem.
    private async Task<(OperatorTokenPurpose Route, string Text)> ArrangeAsync(string reason)
    {
        switch (reason)
        {
            case "wrong_secret":
                return (OperatorTokenPurpose.Bootstrap, OperatorFixture.WithOtherSecret((await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal()));
            case "wrong_purpose":
                return (OperatorTokenPurpose.Recovery, (await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal());
            case "consumed":
                var consumed = (await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal();
                Assert.True((await Redemption().RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(consumed), OperatorFixture.Context(), Token)).Succeeded);
                return (OperatorTokenPurpose.Bootstrap, consumed);
            case "superseded":
                var first = (await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal();
                await IssueAsync(OperatorTokenPurpose.Bootstrap);
                return (OperatorTokenPurpose.Bootstrap, first);
            case "expired":
                var expired = await IssueAsync(OperatorTokenPurpose.Bootstrap);
                await operators.ExpireAsync(expired.TokenId);
                return (OperatorTokenPurpose.Bootstrap, expired.Reveal());
            case "already_initialized":
                var late = (await IssueAsync(OperatorTokenPurpose.Bootstrap)).Reveal();
                await operators.InitializeInstallationAsync();
                return (OperatorTokenPurpose.Bootstrap, late);
            default:
                // A recovery token can be issued only on an initialized installation, so it is inserted directly.
                var recovery = OperatorTokenText.Generate(OperatorTokenPurpose.Recovery);
                await operators.ExecuteAsync(
                    $"INSERT INTO isk_identity.tokens (token_id, purpose, secret_hash, issued_at, expires_at) VALUES ('{recovery.TokenId}', 'recovery', "
                    + $"'\\x{Convert.ToHexStringLower(OperatorTokenHash.Compute(OperatorTokenPurpose.Recovery, recovery))}', now(), now() + interval '1 hour')");
                return (OperatorTokenPurpose.Recovery, new IssuedOperatorToken(recovery, DateTimeOffset.UtcNow).Reveal());
        }
    }

    private Task<IssuedOperatorToken> IssueAsync(OperatorTokenPurpose purpose) =>
        new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(purpose, OperatorFixture.Context(OperatorActor.OperatorCommand), Token).AsTask();

    private PostgreSqlOperatorRedemption Redemption() =>
        new(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes);
}

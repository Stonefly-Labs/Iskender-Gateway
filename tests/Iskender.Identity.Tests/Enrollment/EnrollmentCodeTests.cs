using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Enrollment;

/// <summary>
/// Enrollment codes (ISK-0010 T045, FR-011a, research R11, operator-security contract Enrollment): a redemption's code
/// is consumed once, for its own principal, in the caller's unit, with an <c>enrollment.consumed</c> record; a second
/// consumption, another principal, an expired code, a wrong secret, and a bootstrap token presented as a code each fail
/// with <c>operator.enrollment_invalid</c> and change nothing; of two concurrent consumptions exactly one succeeds; and a
/// consumer unit that rolls back leaves the code unconsumed and no record.
/// </summary>
public class EnrollmentCodeTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PostgreSqlEnrollmentCodes Codes => new(new PostgreSqlAuditRecorder());

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
    public async Task ACodeIsConsumedOnceForItsPrincipalWithOneRecord()
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync();

        await ConsumeAndCommitAsync(principal, code);

        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE token_id = '{OperatorFixture.Parse(code).TokenId}' AND consumed_at IS NOT NULL"));
        Assert.Equal(1L, await operators.AuditCountAsync(
            $"action = 'enrollment.consumed' AND result = 'succeeded' AND reason = 'none' AND actor = '{principal.Value}' AND target_principal = '{principal.Value}'"));

        await AssertInvalidAsync(principal, code);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'enrollment.consumed'"));
    }

    // The prefix is informational and the stored purpose decides (Tokens rule 1): a genuine enrollment code with another
    // prefix is still the code, and is consumed (CONV-0010-006).
    [Fact]
    public async Task AGenuineCodeWithAnotherPrefixIsConsumed()
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync();

        await ConsumeAndCommitAsync(principal, OperatorFixture.Reprefix(code, 'b'));

        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE token_id = '{OperatorFixture.Parse(code).TokenId}' AND consumed_at IS NOT NULL"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'enrollment.consumed' AND result = 'succeeded'"));
    }

    public static TheoryData<string> Refusals() => ["another-principal", "expired", "wrong-secret", "bootstrap-token", "bootstrap-token-reprefixed"];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task AnUnusableCodeFailsWithEnrollmentInvalidAndChangesNothing(string refusal)
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync();
        var presented = code;
        switch (refusal)
        {
            case "another-principal":
                principal = await operators.PrincipalAsync();
                break;
            case "expired":
                await operators.ExpireAsync(OperatorFixture.Parse(code).TokenId);
                break;
            case "wrong-secret":
                presented = OperatorFixture.WithOtherSecret(code);
                break;
            default:
                // A bootstrap token, as issued or re-prefixed as an enrollment code: its stored purpose is bootstrap.
                await operators.ExecuteAsync("DELETE FROM isk_identity.installation");
                presented = (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
                    .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
                presented = refusal == "bootstrap-token-reprefixed" ? OperatorFixture.Reprefix(presented, 'e') : presented;
                break;
        }

        var before = await operators.AllRowsAsync();
        var records = await operators.AuditCountAsync();

        await AssertInvalidAsync(principal, presented);

        Assert.Equal(before, await operators.AllRowsAsync());
        Assert.Equal(records, await operators.AuditCountAsync());
    }

    [Fact]
    public async Task OfTwoConcurrentConsumptionsExactlyOneSucceeds()
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync();
        await using var holder = await operators.OpenAsync();
        await Codes.ConsumeAsync(holder, principal, OperatorFixture.Parse(code), Context(principal), Token);

        // The second waits on the row lock the first holds, then finds the code consumed.
        var waiting = Task.Run(async () =>
        {
            await using var unit = await operators.OpenAsync();
            await Codes.ConsumeAsync(unit, principal, OperatorFixture.Parse(code), Context(principal), Token);
            await unit.CommitAsync(Token);
        }, Token);
        await WhenWaitingOnALockAsync();
        await holder.CommitAsync(Token);

        Assert.Same(ErrorCodes.OperatorEnrollmentInvalid, (await Assert.ThrowsAsync<GatewayException>(() => waiting)).Code);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'enrollment.consumed'"));
    }

    // A consumer that waits on the row lock across the code's expiry: its check, at the start of its locking statement,
    // finds the code unexpired, but its consumption judges expiry by its own clock, so it changes nothing and fails
    // (spec Edge Cases, FR-005; CONV-0010-007).
    [Fact]
    public async Task ACodeThatExpiresWhileItsConsumerWaitsOnTheLockIsNotConsumed()
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync(TimeSpan.FromSeconds(10));
        var tokenId = OperatorFixture.Parse(code).TokenId;
        var holder = await operators.OpenAsync();
        await Codes.ConsumeAsync(holder, principal, OperatorFixture.Parse(code), Context(principal), Token);

        var waiting = Task.Run(async () =>
        {
            await using var unit = await operators.OpenAsync();
            await Codes.ConsumeAsync(unit, principal, OperatorFixture.Parse(code), Context(principal), Token);
            await unit.CommitAsync(Token);
        }, Token);
        await WhenWaitingOnALockAsync();

        // The waiter's check started before the expiry; wait, by the database's clock, until it has passed, then let the
        // holder roll back, so the waiter finds the code unconsumed.
        var expiry = $"SELECT clock_timestamp() > expires_at FROM isk_identity.tokens WHERE token_id = '{tokenId}'";
        Assert.False(await operators.ScalarAsync<bool>(expiry));
        for (var attempt = 0; attempt < 300 && !await operators.ScalarAsync<bool>(expiry); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        await holder.DisposeAsync();

        Assert.Same(ErrorCodes.OperatorEnrollmentInvalid, (await Assert.ThrowsAsync<GatewayException>(() => waiting)).Code);
        Assert.Equal(0L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE token_id = '{tokenId}' AND consumed_at IS NOT NULL"));
        Assert.Equal(0L, await operators.AuditCountAsync("action = 'enrollment.consumed'"));
    }

    [Fact]
    public async Task AConsumerUnitThatRollsBackLeavesTheCodeUnconsumedAndNoRecord()
    {
        operators.RequirePostgreSql();
        var (principal, code) = await RedeemAsync();

        await using (var unit = await operators.OpenAsync())
        {
            await Codes.ConsumeAsync(unit, principal, OperatorFixture.Parse(code), Context(principal), Token);
        }

        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE purpose = 'enrollment' AND consumed_at IS NOT NULL"));
        Assert.Equal(0L, await operators.AuditCountAsync("action = 'enrollment.consumed'"));
        await ConsumeAndCommitAsync(principal, code);
    }

    private static OperatorRequestContext Context(ResourceId principal) => OperatorFixture.Context(OperatorActor.ForPrincipal(principal));

    private async Task<(ResourceId Principal, string Code)> RedeemAsync(TimeSpan? codeLifetime = null)
    {
        var configured = OperatorFixture.Configuration().Lifetimes;
        var lifetimes = codeLifetime is { } lifetime ? configured with { EnrollmentCodeLifetime = lifetime } : configured;
        var token = (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
        var outcome = await new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes)
            .RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(token), OperatorFixture.Context(), Token);
        Assert.True(outcome.Succeeded);
        return (outcome.Principal!, outcome.EnrollmentCode!.Reveal());
    }

    private async Task ConsumeAndCommitAsync(ResourceId principal, string code)
    {
        await using var unit = await operators.OpenAsync();
        await Codes.ConsumeAsync(unit, principal, OperatorFixture.Parse(code), Context(principal), Token);
        await unit.CommitAsync(Token);
    }

    private async Task AssertInvalidAsync(ResourceId principal, string code)
    {
        await using var unit = await operators.OpenAsync();
        var failure = await Assert.ThrowsAsync<GatewayException>(() => Codes.ConsumeAsync(unit, principal, OperatorFixture.Parse(code), Context(principal), Token).AsTask());
        Assert.Same(ErrorCodes.OperatorEnrollmentInvalid, failure.Code);
    }

    private async Task WhenWaitingOnALockAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await operators.ScalarAsync<long>($"SELECT count(*) FROM pg_stat_activity WHERE datname = '{operators.DatabaseName}' AND wait_event_type = 'Lock'") > 0)
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        Assert.Fail("The second consumption never waited on the row lock.");
    }
}

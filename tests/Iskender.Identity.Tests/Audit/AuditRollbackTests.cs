using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Audit;

/// <summary>
/// ISK-0010 T041, US5.2, FR-026, operator-security contract Audit records rule 1: a redemption whose unit fails after
/// the success record's insert, through the redemption's test seam, commits nothing, so it leaves no success record, no
/// principal, no grant, no enrollment code, and no installation row, and the token stays redeemable.
/// </summary>
public class AuditRollbackTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task AFailureAfterTheSuccessRecordLeavesNoRecordPrincipalOrInstallation()
    {
        operators.RequirePostgreSql();
        var lifetimes = OperatorFixture.Configuration().Lifetimes;
        var text = (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
        var written = false;
        var failing = new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes, (step, _) =>
        {
            if (step == "written")
            {
                written = true;
                throw new InvalidOperationException("The unit fails after the success record's insert.");
            }

            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(text), OperatorFixture.Context(), Token).AsTask());

        Assert.True(written);
        Assert.Equal(0L, await operators.AuditCountAsync("action = 'bootstrap.redeemed'"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.operator_grants"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE purpose = 'enrollment' OR consumed_at IS NOT NULL"));

        var redeemed = await new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), lifetimes)
            .RedeemAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Parse(text), OperatorFixture.Context(), Token);
        Assert.True(redeemed.Succeeded);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'succeeded'"));
    }
}

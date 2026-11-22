using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Tokens;

/// <summary>
/// The token issuer through the store-level fixture (ISK-0010 T015, research R6, operator-security contract Tokens rules
/// 2 to 6, FR-003 to FR-006): one issue stores a hash and no secret, with one issue record; a second supersedes the first
/// with one supersede record; two concurrent issues both succeed and leave one active token; each refused state fails with
/// its code and writes nothing; and the expiry is measured from the database's clock.
/// </summary>
public class PostgreSqlOperatorTokensTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OperatorRequestContext Command => OperatorFixture.Context(OperatorActor.OperatorCommand);

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
    public async Task AnIssueStoresTheHashAndNoSecretWithOneIssueRecord()
    {
        operators.RequirePostgreSql();

        var issued = await Tokens().IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token);
        var text = issued.Reveal();

        Assert.True(OperatorTokenText.TryParse(text, out var parsed));
        Assert.Equal(OperatorTokenPurpose.Bootstrap, issued.Purpose);
        Assert.Equal(0, issued.Superseded);
        Assert.Equal(
            $"{issued.TokenId}|bootstrap|{Convert.ToHexStringLower(OperatorTokenHash.Compute(OperatorTokenPurpose.Bootstrap, parsed!))}|null|active",
            await operators.ScalarAsync<string>(
                "SELECT token_id || '|' || purpose || '|' || encode(secret_hash, 'hex') || '|' || coalesce(principal_id, 'null') || '|' "
                + "|| CASE WHEN consumed_at IS NULL AND superseded_at IS NULL THEN 'active' ELSE 'inactive' END FROM isk_identity.tokens"));
        var secret = text[(text.LastIndexOf('_') + 1)..];
        Assert.DoesNotContain(secret, await operators.AllRowsAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(text, await operators.AllRowsAsync(), StringComparison.Ordinal);
        Assert.Equal(
            "operator_command|bootstrap_token.issued|succeeded|none",
            await operators.ScalarAsync<string>("SELECT actor || '|' || action || '|' || result || '|' || reason FROM isk_audit.records"));
    }

    [Fact]
    public async Task ASecondIssueSupersedesTheFirstWithOneSupersedeRecord()
    {
        operators.RequirePostgreSql();
        var tokens = Tokens();

        var first = await tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token);
        var second = await tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token);

        Assert.Equal(1, second.Superseded);
        Assert.Equal(
            $"{first.TokenId}:superseded,{second.TokenId}:active",
            await operators.ScalarAsync<string>(
                "SELECT string_agg(token_id || ':' || CASE WHEN superseded_at IS NOT NULL THEN 'superseded' ELSE 'active' END, ',' ORDER BY issued_at, token_id) FROM isk_identity.tokens"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'token.superseded' AND actor = 'operator_command' AND result = 'succeeded' AND reason = 'none'"));
        Assert.Equal(2L, await operators.AuditCountAsync("action = 'bootstrap_token.issued'"));
    }

    [Fact]
    public async Task TwoConcurrentIssuesBothSucceedAndLeaveOneActiveToken()
    {
        operators.RequirePostgreSql();
        var tokens = Tokens();

        var issued = await Task.WhenAll(
            tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token).AsTask(),
            tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token).AsTask());

        Assert.Equal([0, 1], issued.Select(token => token.Superseded).Order());
        Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens WHERE consumed_at IS NULL AND superseded_at IS NULL"));
        Assert.Equal(2L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.tokens"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'token.superseded'"));
    }

    [Fact]
    public async Task BootstrapOnAnInitializedInstallationFailsAndWritesNothing()
    {
        operators.RequirePostgreSql();
        await operators.ExecuteAsync("INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', now(), 'test')");
        var before = await operators.AllRowsAsync();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Tokens().IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token));

        Assert.Same(ErrorCodes.OperatorAlreadyInitialized, failure.Code);
        Assert.Equal(before, await operators.AllRowsAsync());
    }

    [Fact]
    public async Task RecoveryOnAnUninitializedInstallationFailsAndWritesNothing()
    {
        operators.RequirePostgreSql();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Tokens().IssueAsync(OperatorTokenPurpose.Recovery, Command, Token));

        Assert.Same(ErrorCodes.OperatorNotInitialized, failure.Code);
        Assert.Equal(string.Join("\n", Enumerable.Repeat("[]", OperatorFixture.IdentityTableNames.Count)), await operators.AllRowsAsync());
    }

    [Fact]
    public async Task RecoveryOnAnInitializedInstallationIssuesARecoveryToken()
    {
        operators.RequirePostgreSql();
        await operators.ExecuteAsync("INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', now(), 'test')");

        var issued = await Tokens().IssueAsync(OperatorTokenPurpose.Recovery, Command, Token);

        Assert.StartsWith("iskr_", issued.Reveal(), StringComparison.Ordinal);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery_token.issued'"));
    }

    [Fact]
    public async Task EnrollmentAndANullContextFailBeforeAnyStatement()
    {
        // The unreachable factory proves no unit is opened: an open would fail with persistence.unavailable.
        operators.RequirePostgreSql();
        var tokens = new PostgreSqlOperatorTokens(operators.UnreachableFactory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes);

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await tokens.IssueAsync(OperatorTokenPurpose.Enrollment, Command, Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, null!, Token))).Code);
    }

    [Fact]
    public async Task TheExpiryIsMeasuredFromTheDatabaseClock()
    {
        operators.RequirePostgreSql();

        var issued = await Tokens("\"bootstrap\":{\"tokenLifetime\":\"10m\"}").IssueAsync(OperatorTokenPurpose.Bootstrap, Command, Token);

        Assert.Equal(600d, await operators.ScalarAsync<double>("SELECT extract(epoch FROM expires_at - issued_at)::float8 FROM isk_identity.tokens"));
        Assert.Equal(
            await operators.ScalarAsync<DateTime>("SELECT expires_at FROM isk_identity.tokens"),
            issued.ExpiresAt.UtcDateTime);
    }

    private PostgreSqlOperatorTokens Tokens(string operatorFields = "") =>
        new(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration(operatorFields).Lifetimes);
}

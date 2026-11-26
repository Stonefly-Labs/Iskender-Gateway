using System.Net;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Audit;

/// <summary>
/// The audit trail (ISK-0010 T040, US5.1, FR-027, FR-028, operator-security contract Audit records rules 2 and 4): an
/// issue, its supersede, a failed redemption naming a stored token, a successful bootstrap redemption, a recovery issue,
/// and a recovery redemption each leave exactly one record with its actor, target, action, scope, result, reason,
/// configuration revision, and correlation ID, and no record holds a token, a hash, a network address, or a request body.
/// </summary>
public class AuditTrailTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const string Columns =
        "actor || '|' || coalesce(target_principal, '-') || '|' || action || '|' || scope || '|' || result || '|' || reason || '|' || configuration_revision || '|' || correlation_id";

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
    public async Task EachAuditedChangeLeavesExactlyOneRecordWithItsFields()
    {
        operators.RequirePostgreSql();
        var tokens = new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes);
        await using var host = await operators.HostAsync();
        var revision = host.Services.GetRequiredService<IValidatedConfiguration>().Snapshot.Revision.Value;

        var first = OperatorFixture.Context(OperatorActor.OperatorCommand);
        var superseded = (await tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, first, Token)).Reveal();
        var second = OperatorFixture.Context(OperatorActor.OperatorCommand);
        var current = (await tokens.IssueAsync(OperatorTokenPurpose.Bootstrap, second, Token)).Reveal();

        using var refused = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/bootstrap", superseded);
        var refusedCorrelation = await AdminRouteTests.AssertErrorAsync(refused, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        var bootstrapPrincipal = await PrincipalAsync(await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/bootstrap", current));

        var third = OperatorFixture.Context(OperatorActor.OperatorCommand);
        var recovery = (await tokens.IssueAsync(OperatorTokenPurpose.Recovery, third, Token)).Reveal();
        var recoveryPrincipal = await PrincipalAsync(await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/recovery", recovery));

        var records = await RecordsAsync();
        var bootstrapCorrelation = await CorrelationAsync($"action = 'bootstrap.redeemed' AND result = 'succeeded'");
        var recoveryCorrelation = await CorrelationAsync($"action = 'recovery.redeemed'");
        Assert.Equal(
            new[]
            {
                $"operator_command|-|bootstrap_token.issued|operator|succeeded|none|test-rev|{first.CorrelationId.Value}",
                $"operator_command|-|bootstrap_token.issued|operator|succeeded|none|test-rev|{second.CorrelationId.Value}",
                $"operator_command|-|token.superseded|operator|succeeded|none|test-rev|{second.CorrelationId.Value}",
                $"anonymous|-|bootstrap.redeemed|operator|failed|superseded|{revision}|{refusedCorrelation}",
                $"anonymous|{bootstrapPrincipal}|bootstrap.redeemed|operator|succeeded|none|{revision}|{bootstrapCorrelation}",
                $"operator_command|-|recovery_token.issued|operator|succeeded|none|test-rev|{third.CorrelationId.Value}",
                $"anonymous|{recoveryPrincipal}|recovery.redeemed|operator|succeeded|none|{revision}|{recoveryCorrelation}",
            }.Order(StringComparer.Ordinal),
            records.Order(StringComparer.Ordinal));
        Assert.Equal(7, (await operators.ScalarAsync<long>("SELECT count(DISTINCT audit_id) FROM isk_audit.records")));
        Assert.NotEqual(bootstrapCorrelation, recoveryCorrelation);
        Assert.NotEqual(bootstrapPrincipal, recoveryPrincipal);

        // FR-027: no column holds a token, a secret, a stored hash, a network address, or a request body.
        var everything = await operators.ScalarAsync<string>("SELECT string_agg(r::text, E'\\n') FROM isk_audit.records r");
        var hashes = await operators.ScalarAsync<string>("SELECT string_agg(encode(secret_hash, 'hex') || ',' || encode(secret_hash, 'base64'), ',') FROM isk_identity.tokens");
        foreach (var text in new[] { superseded, current, recovery })
        {
            Assert.DoesNotContain(text, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(OperatorFixture.Secret(text), everything, StringComparison.Ordinal);
        }

        Assert.All(hashes.Split(','), hash => Assert.DoesNotContain(hash, everything, StringComparison.Ordinal));
        Assert.DoesNotContain("127.0.0.1", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("::1", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", everything, StringComparison.Ordinal);
    }

    private async Task<List<string>> RecordsAsync()
    {
        var rows = await operators.ScalarAsync<string>($"SELECT string_agg({Columns}, E'\\n') FROM isk_audit.records");
        return [.. rows.Split('\n')];
    }

    private Task<string> CorrelationAsync(string where) =>
        operators.ScalarAsync<string>($"SELECT correlation_id FROM isk_audit.records WHERE {where}");

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

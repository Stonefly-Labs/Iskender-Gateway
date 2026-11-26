using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Identity.Tests.Redemption;

/// <summary>
/// Bootstrap redemption over HTTP (ISK-0010 T025, US2.1, US2.2, US2.4, FR-008, FR-010, FR-011, SC-003): a valid token
/// initializes the installation once; every refusal answers the same <c>403</c> body apart from its correlation ID, and
/// writes a failure record naming its reason exactly when the token is stored; and the installation route then passes
/// the gate. The same holds for <c>consumed</c>, <c>wrong_purpose</c> at both routes, <c>already_initialized</c>,
/// <c>not_initialized</c>, and a rejected body (T078, CONV-0010-020).
/// </summary>
public class BootstrapRedemptionTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task AValidTokenInitializesOnceAndASecondRedemptionFailsAsConsumed()
    {
        operators.RequirePostgreSql();
        var token = (await IssueAsync()).Reveal();
        await using var host = await operators.HostAsync(new OperatorHostOptions());

        using var response = await RedeemAsync(host, "/admin/v1/bootstrap", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(["principalId", "enrollmentCode"], body.RootElement.EnumerateObject().Select(property => property.Name));
        var principal = body.RootElement.GetProperty("principalId").GetString()!;
        Assert.StartsWith("prin_", principal, StringComparison.Ordinal);
        Assert.StartsWith("iske_", body.RootElement.GetProperty("enrollmentCode").GetString(), StringComparison.Ordinal);
        Assert.Equal(principal, await operators.ScalarAsync<string>("SELECT initialized_by FROM isk_identity.installation"));
        Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.operator_grants WHERE principal_id = '{principal}' AND role = 'system_operator'"));
        Assert.Equal(1L, await operators.ScalarAsync<long>($"SELECT count(*) FROM isk_identity.tokens WHERE purpose = 'enrollment' AND principal_id = '{principal}'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'succeeded'"));
        Assert.Equal("bootstrap", Assert.Single(host.Host.Logs.ById(9001)).Value("Purpose"));

        using var again = await RedeemAsync(host, "/admin/v1/bootstrap", token);

        await AdminRouteTests.AssertErrorAsync(again, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'consumed'"));
        Assert.Equal(1L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.principals"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/installation");
        request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
        using var installation = await host.Client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, installation.StatusCode);
    }

    [Fact]
    public async Task EveryRefusalAnswersTheSameBodyApartFromItsCorrelationId()
    {
        operators.RequirePostgreSql();
        // Each issue supersedes the earlier tokens, and superseded is checked before expiry, so the expired token is the
        // last issued. The wrong secret is checked before either, so its token may be superseded.
        var superseded = (await IssueAsync()).Reveal();
        var current = (await IssueAsync()).Reveal();
        var expired = await IssueAsync();
        await operators.ExpireAsync(expired.TokenId);
        var unknown = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal();
        var cases = new (string Text, string? Reason)[]
        {
            (expired.Reveal(), "expired"),
            (superseded, "superseded"),
            (OperatorFixture.WithOtherSecret(current), "wrong_secret"),
            (unknown, null),
            ("iskb_not-a-token", null),
        };
        await using var host = await operators.HostAsync(new OperatorHostOptions());

        var bodies = new List<string>();
        foreach (var (text, reason) in cases)
        {
            var records = await operators.AuditCountAsync();
            using var response = await RedeemAsync(host, "/admin/v1/bootstrap", text);

            var correlation = await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
            bodies.Add((await response.Content.ReadAsStringAsync(Token)).Replace(correlation, "<correlation>", StringComparison.Ordinal));
            Assert.Equal(records + (reason is null ? 0 : 1), await operators.AuditCountAsync());
            if (reason is not null)
            {
                Assert.Equal(1L, await operators.AuditCountAsync($"action = 'bootstrap.redeemed' AND result = 'failed' AND reason = '{reason}'"));
            }
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal(["expired", "superseded", "wrong_secret", "unknown", "malformed"], host.Host.Logs.ById(9002).Select(logged => logged.Value("Reason")));
        Assert.Equal(0L, await operators.ScalarAsync<long>("SELECT count(*) FROM isk_identity.installation"));
    }

    [Fact]
    public async Task EveryOtherRefusalOverHttpAnswersTheSameBodyApartFromItsCorrelationId()
    {
        operators.RequirePostgreSql();
        // A recovery token is issued only on an initialized installation, so the installation is initialized for it and
        // then emptied again.
        var bootstrap = (await IssueAsync()).Reveal();
        await operators.InitializeInstallationAsync();
        var recovery = (await IssueAsync(OperatorTokenPurpose.Recovery)).Reveal();
        await operators.ExecuteAsync("DELETE FROM isk_identity.installation");
        await using var host = await operators.HostAsync(new OperatorHostOptions());
        var bodies = new List<string>();

        async Task RefusedAsync(Task<HttpResponseMessage> sending)
        {
            using var response = await sending;
            var correlation = await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OperatorRedemptionFailed);
            bodies.Add((await response.Content.ReadAsStringAsync(Token)).Replace(correlation, "<correlation>", StringComparison.Ordinal));
        }

        await RefusedAsync(RedeemAsync(host, "/admin/v1/bootstrap", "iskb_not-a-token"));
        await RefusedAsync(host.Client.PostAsync("/admin/v1/bootstrap", new StringContent("{\"token\":1}", Encoding.UTF8, new MediaTypeHeaderValue("application/json")), Token));
        await RefusedAsync(RedeemAsync(host, "/admin/v1/recovery", recovery));
        await RefusedAsync(RedeemAsync(host, "/admin/v1/recovery", bootstrap));
        await RefusedAsync(RedeemAsync(host, "/admin/v1/bootstrap", recovery));

        using (var redeemed = await RedeemAsync(host, "/admin/v1/bootstrap", bootstrap))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        }

        await RefusedAsync(RedeemAsync(host, "/admin/v1/bootstrap", bootstrap));

        // A bootstrap token is issued only on an uninitialized installation, so the installation is emptied for it and
        // then initialized again.
        await operators.ExecuteAsync("DELETE FROM isk_identity.installation");
        var late = (await IssueAsync()).Reveal();
        await operators.InitializeInstallationAsync();
        await RefusedAsync(RedeemAsync(host, "/admin/v1/bootstrap", late));

        Assert.Equal(7, bodies.Count);
        Assert.Single(bodies.Distinct());
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'not_initialized'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'recovery.redeemed' AND result = 'failed' AND reason = 'wrong_purpose'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'wrong_purpose'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'consumed'"));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap.redeemed' AND result = 'failed' AND reason = 'already_initialized'"));
    }

    internal static Task<HttpResponseMessage> RedeemAsync(OperatorHost host, string path, string token)
    {
        var content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return host.Client.PostAsync(path, content, Token);
    }

    private Task<IssuedOperatorToken> IssueAsync(OperatorTokenPurpose purpose = OperatorTokenPurpose.Bootstrap) =>
        new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(purpose, OperatorFixture.Context(OperatorActor.OperatorCommand), Token).AsTask();
}

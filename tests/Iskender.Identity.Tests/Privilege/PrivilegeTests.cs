using System.Net;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Privilege;

/// <summary>
/// Operator privilege on an initialized installation (ISK-0010 T029, US3.1 to US3.4, FR-015, FR-036a, SC-006, research
/// R10, spec Edge Cases): no credential is <c>401</c>; a principal with no grant is <c>403</c>; the principal a
/// redemption created reads the installation, and the answer holds no principal ID; unreadable grants or a throwing
/// resolver are <c>503</c> and run no handler; and a grant inserted directly takes effect on the next request.
/// </summary>
public class PrivilegeTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task NoCredentialIsUnauthenticatedAndAPrincipalWithNoGrantIsDenied()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var ungranted = await operators.PrincipalAsync(granted: false);
        await using var host = await operators.HostAsync();

        using var anonymous = await host.Client.GetAsync("/admin/v1/installation", Token);
        using var denied = await GetAsync(host, ungranted.Value);

        await AdminRouteTests.AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
        await AdminRouteTests.AssertErrorAsync(denied, HttpStatusCode.Forbidden, ErrorCodes.IdentityAuthDenied);
        Assert.Equal(["unauthenticated", "forbidden"], host.Host.Logs.ById(9003).Select(logged => logged.Value("Reason")));
    }

    [Fact]
    public async Task ThePrincipalARedemptionCreatedReadsTheInstallationWithNoPrincipalId()
    {
        operators.RequirePostgreSql();
        var token = (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
        await using var host = await operators.HostAsync();
        using var redeemed = await BootstrapRedemptionTests.RedeemAsync(host, "/admin/v1/bootstrap", token);
        using var created = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync(Token));
        var principal = created.RootElement.GetProperty("principalId").GetString()!;

        using var response = await GetAsync(host, principal);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(["state", "initializedAt"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("initialized", body.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            await operators.ScalarAsync<DateTime>("SELECT initialized_at AT TIME ZONE 'UTC' FROM isk_identity.installation"),
            body.RootElement.GetProperty("initializedAt").GetDateTimeOffset().UtcDateTime);
        Assert.DoesNotContain(principal, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableGrantsAfterTheStateWasCachedAre503AndRunNoHandler()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        var grants = new SwitchableAuthorization(new PostgreSqlOperatorAuthorization(operators.Factory));
        var ran = 0;
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Services = services => services.AddSingleton<IOperatorAuthorization>(grants),
            Map = app => app.MapIskenderOperatorSecurity("/admin/v1")
                .MapGet("/counted", (HttpContext _) => Interlocked.Increment(ref ran))
                .RequireOperatorAction(OperatorActions.InstallationRead),
        });
        using (var cached = await GetAsync(host, principal.Value, "/admin/v1/counted"))
        {
            Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        }

        grants.Fail = true;
        using var response = await GetAsync(host, principal.Value, "/admin/v1/counted");

        await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable);
        Assert.Equal(1, ran);
        Assert.Equal("operator.authorization_unavailable", host.Host.Logs.Only(9008).Value("Code"));
    }

    [Fact]
    public async Task AThrowingResolverIs503WithOnlyTheExceptionTypeName()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        await using var host = await operators.HostAsync();
        host.Resolver.Throw = new InvalidOperationException("resolver detail " + principal.Value);

        using var response = await GetAsync(host, principal.Value);

        await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable);
        var logged = host.Host.Logs.Only(9007);
        Assert.Equal("InvalidOperationException", logged.Value("Failure"));
        Assert.DoesNotContain("resolver detail", logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(principal.Value, logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGrantInsertedDirectlyTakesEffectOnTheNextRequest()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync(granted: false);
        await using var host = await operators.HostAsync();
        using (var before = await GetAsync(host, principal.Value))
        {
            await AdminRouteTests.AssertErrorAsync(before, HttpStatusCode.Forbidden, ErrorCodes.IdentityAuthDenied);
        }

        await operators.ExecuteAsync($"INSERT INTO isk_identity.operator_grants (principal_id, role, granted_at) VALUES ('{principal.Value}', 'system_operator', now())");
        using var after = await GetAsync(host, principal.Value);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private static Task<HttpResponseMessage> GetAsync(OperatorHost host, string principal, string path = "/admin/v1/installation")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
        return host.Client.SendAsync(request, Token);
    }

    /// <summary>The real grant store, which can be switched to fail as an unreadable one does.</summary>
    private sealed class SwitchableAuthorization(IOperatorAuthorization inner) : IOperatorAuthorization
    {
        public bool Fail { get; set; }

        public ValueTask<OperatorDecision> DecideAsync(ResourceId principal, OperatorAction action, CancellationToken cancellationToken) =>
            Fail ? throw new GatewayException(ErrorCodes.PersistenceUnavailable) : inner.DecideAsync(principal, action, cancellationToken);
    }
}

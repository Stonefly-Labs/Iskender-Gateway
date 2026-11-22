using System.Net;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;
using Iskender.Identity.Tests.Redemption;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management;
using Iskender.Management.Security;

namespace Iskender.Identity.Tests.Embedding;

/// <summary>
/// Embedding the operator baseline (ISK-0010 T047, US7.4, FR-030, hosting contract Test fixtures rule 4, Lifetimes and
/// Static state): an <see cref="EmbeddedHost"/> composing <c>AddIskenderControlPlane(...)</c>,
/// <c>AddIskenderOperatorSecurity()</c>, <c>AddIskenderPostgreSqlIdentity()</c>, <c>AddIskenderPostgreSqlPersistence()</c>,
/// and <c>AddIskenderPostgreSqlWork()</c> under its own prefix passes the gate, redemption, and privilege scenarios; the
/// operator module under the data plane's or the worker's root fails start with <c>hosting.registration_missing</c>; and
/// two embedded hosts in one process each have their own attempt limiter.
/// </summary>
public class EmbeddedControlPlaneTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const string Prefix = "/platform/operators";

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
    public async Task AnEmbeddedHostUnderItsOwnPrefixPassesTheGateRedemptionAndPrivilegeScenarios()
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync(new OperatorHostOptions { Prefix = Prefix });
        var ungranted = await operators.PrincipalAsync(granted: false);

        // The gate: before the first operator, the installation route is denied, under this prefix only.
        using var uninitialized = await ReadInstallationAsync(host, ungranted.Value);
        await AdminRouteTests.AssertErrorAsync(uninitialized, HttpStatusCode.Forbidden, ErrorCodes.OperatorUninitialized);
        using var defaultPrefix = await host.Client.GetAsync("/admin/v1/installation", Token);
        Assert.Equal(HttpStatusCode.NotFound, defaultPrefix.StatusCode);

        // The redemption: a bootstrap token initializes the installation through this prefix's route.
        var token = (await new PostgreSqlOperatorTokens(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .IssueAsync(OperatorTokenPurpose.Bootstrap, OperatorFixture.Context(OperatorActor.OperatorCommand), Token)).Reveal();
        using var redeemed = await BootstrapRedemptionTests.RedeemAsync(host, $"{Prefix}/bootstrap", token);
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        using var body = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync(Token));
        var principal = body.RootElement.GetProperty("principalId").GetString()!;

        // The privilege: the new operator reads the installation, a principal with no grant is denied, no credential is unauthenticated.
        using var granted = await ReadInstallationAsync(host, principal);
        using var denied = await ReadInstallationAsync(host, ungranted.Value);
        using var anonymous = await host.Client.GetAsync($"{Prefix}/installation", Token);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        await AdminRouteTests.AssertErrorAsync(denied, HttpStatusCode.Forbidden, ErrorCodes.IdentityAuthDenied);
        await AdminRouteTests.AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
    }

    [Theory]
    [InlineData("data-plane")]
    [InlineData("worker")]
    public async Task TheOperatorModuleUnderAnotherRootFailsStartWithRegistrationMissing(string root)
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                if (root == "data-plane")
                {
                    builder.Services.AddIskender(_ => { });
                }
                else
                {
                    builder.Services.AddIskenderWorker(_ => { });
                }

                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderPostgreSqlWork();
                builder.Services.AddIskenderPostgreSqlIdentity();
                builder.Services.AddIskenderOperatorSecurity();
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("missing", ControlPlaneServiceCollectionExtensions.ControlPlaneModule), failure.Error!.Diagnostics!.Values);
    }

    [Fact]
    public async Task TwoEmbeddedHostsInOneProcessEachHaveTheirOwnAttemptLimiter()
    {
        operators.RequirePostgreSql();
        var options = new OperatorHostOptions { Prefix = Prefix, Unreachable = true, OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":1}" };
        await using var first = await operators.HostAsync(options);
        await using var second = await operators.HostAsync(options);

        using var admitted = await RedeemUnknownAsync(first);
        using var limited = await RedeemUnknownAsync(first);
        using var other = await RedeemUnknownAsync(second);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, admitted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
    }

    private static Task<HttpResponseMessage> ReadInstallationAsync(OperatorHost host, string principal)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{Prefix}/installation");
        request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
        return host.Client.SendAsync(request, Token);
    }

    private static Task<HttpResponseMessage> RedeemUnknownAsync(OperatorHost host) =>
        BootstrapRedemptionTests.RedeemAsync(host, $"{Prefix}/bootstrap", new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal());
}

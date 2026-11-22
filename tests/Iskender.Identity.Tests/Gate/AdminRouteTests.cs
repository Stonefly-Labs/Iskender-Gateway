using System.Net;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Gate;

/// <summary>
/// The admin route group and its pipeline past the gate (ISK-0010 T019, operator-security contract Composition rule 3,
/// Admin routes rules 1 to 9, FR-017, FR-036a): the prefix rule, mapping without the registration, unmatched requests,
/// and each answer of <c>GET &lt;prefix&gt;/installation</c> on an initialized installation.
/// </summary>
public class AdminRouteTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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

    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("/")]
    [InlineData("/Admin")]
    [InlineData("/admin v1")]
    [InlineData("/admin_v1")]
    public void APrefixOutsideThePatternIsRefusedAtTheCall(string prefix)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddIskenderOperatorSecurity();
        using var app = builder.Build();

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => app.MapIskenderOperatorSecurity(prefix)).Code);
    }

    [Fact]
    public void ALongestPrefixIsAcceptedAndOneLongerRefused()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddIskenderOperatorSecurity();
        using var app = builder.Build();

        app.MapIskenderOperatorSecurity("/" + new string('a', 64));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => app.MapIskenderOperatorSecurity("/" + new string('a', 65))).Code);
    }

    [Fact]
    public void MappingWithoutTheRegistrationFailsWithRegistrationMissing()
    {
        using var app = WebApplication.CreateBuilder().Build();

        Assert.Same(ErrorCodes.HostingRegistrationMissing, Assert.Throws<GatewayException>(() => app.MapIskenderOperatorSecurity("/admin/v1")).Code);
    }

    [Theory]
    [InlineData("GET", "/admin/v1/bootstrap")]
    [InlineData("POST", "/admin/v1/installation")]
    [InlineData("DELETE", "/admin/v1/installation")]
    [InlineData("GET", "/admin/v1/nothing/here")]
    [InlineData("GET", "/admin/v1")]
    public async Task ARequestMatchingNoRouteAnswers404WithNoBody(string method, string path)
    {
        operators.RequirePostgreSql();
        await using var host = await operators.HostAsync();

        using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(0, host.Resolver.Calls);
    }

    [Fact]
    public async Task AGrantedPrincipalReadsTheInstallationWithNoStoreAndNoPrincipalId()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        await using var host = await operators.HostAsync();

        using var response = await host.Client.SendAsync(Get(principal.Value), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.Equal("{\"state\":\"initialized\",\"initializedAt\":\"2026-11-21T10:00:00.5Z\"}", body);
        Assert.DoesNotContain(principal.Value, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoResolverComposedEveryRequestIsUnauthenticated()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions { WithResolver = false });

        using var response = await host.Client.SendAsync(Get(principal.Value), Token);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
        Assert.Equal("unauthenticated", host.Host.Logs.Only(9003).Value("Reason"));
    }

    public static TheoryData<string> Unresolved() => ["", "not-an-id", "org_0000000000000000000000000a"];

    [Theory]
    [MemberData(nameof(Unresolved))]
    public async Task AnUnresolvedRequestIsUnauthenticated(string header)
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        await using var host = await operators.HostAsync();

        using var response = await host.Client.SendAsync(Get(header), Token);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
    }

    [Fact]
    public async Task APrincipalWithoutTheGrantIsDenied()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync(granted: false);
        await using var host = await operators.HostAsync();

        using var response = await host.Client.SendAsync(Get(principal.Value), Token);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.IdentityAuthDenied);
        var logged = host.Host.Logs.Only(9003);
        Assert.Equal(("/admin/v1/installation", "forbidden"), (logged.Value("Route"), logged.Value("Reason")));
    }

    [Fact]
    public async Task AResolverThatThrowsAnswers503WithOnlyItsTypeName()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        await using var host = await operators.HostAsync();
        host.Resolver.Throw = new InvalidOperationException("secret detail");

        using var response = await host.Client.SendAsync(Get(""), Token);

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable);
        Assert.Equal("InvalidOperationException", host.Host.Logs.Only(9007).Value("Failure"));
        Assert.DoesNotContain(host.Host.Logs.Entries, entry => entry.Message.Contains("secret detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreadableGrantsAnswer503()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Services = services => services.AddSingleton<IOperatorAuthorization>(new ThrowingAuthorization()),
        });

        using var response = await host.Client.SendAsync(Get(principal.Value), Token);

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorAuthorizationUnavailable);
        Assert.Equal("operator.authorization_unavailable", host.Host.Logs.Only(9008).Value("Code"));
    }

    [Fact]
    public async Task AnEndpointMappedAsAPlainDelegateIsWrappedToo()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Map = app => app.MapIskenderOperatorSecurity("/admin/v1")
                .Map("/plain", (Microsoft.AspNetCore.Http.RequestDelegate)(http => http.Response.WriteAsync("ran", http.RequestAborted)))
                .RequireOperatorAction(OperatorActions.InstallationRead),
        });

        using var response = await host.Client.GetAsync("/admin/v1/plain", Token);

        await AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
    }

    private static HttpRequestMessage Get(string principal)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/installation");
        if (principal.Length > 0)
        {
            request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
        }

        return request;
    }

    /// <summary>Asserts the answer is the <c>error-envelope</c> for <paramref name="code"/>, and returns its correlation ID.</summary>
    internal static async Task<string> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, ErrorCode code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(code.Id, envelope.RootElement.GetProperty("code").GetString());
        var correlation = envelope.RootElement.GetProperty("correlation_id").GetString()!;
        Assert.StartsWith("req_", correlation, StringComparison.Ordinal);
        return correlation;
    }

    private sealed class ThrowingAuthorization : IOperatorAuthorization
    {
        public ValueTask<OperatorDecision> DecideAsync(Iskender.Core.Contracts.Identifiers.ResourceId principal, OperatorAction action, CancellationToken cancellationToken) =>
            throw new GatewayException(ErrorCodes.PersistenceUnavailable);
    }
}

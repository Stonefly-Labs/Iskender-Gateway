using System.Net;
using System.Net.Http.Json;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;

namespace Iskender.Identity.Tests.Gate;

/// <summary>
/// A protected route handler that binds a JSON body, a route value, and a query parameter (ISK-0010 T062, FR-018,
/// operator-security contract Admin routes rules 3 and 6, US1.4, research R8; CONV-0010-004): the pipeline runs before the
/// delegate factory binds anything, so on an uninitialized installation, with an unreadable state, and without a
/// credential or a grant, a request whose body is the wrong type or malformed JSON gets the pipeline's answer, never
/// <c>415</c> or <c>400</c>, and the handler never runs. Once admitted, the handler binds every value.
/// </summary>
public class BindingHandlerTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private const string Route = "/admin/v1/bound/item-7?count=3";

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

    // Bodies the delegate factory would refuse before the handler: the wrong media type (415) and malformed JSON (400).
    public static TheoryData<string> Bodies() => ["text", "malformed", "valid"];

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task OnAnUninitializedInstallationEveryBodyGetsTheUninitializedAnswer(string body)
    {
        operators.RequirePostgreSql();
        var handler = new Handler();
        await using var host = await operators.HostAsync(handler.Options());

        using var response = await host.Client.SendAsync(Request(body, credential: null), Token);

        await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OperatorUninitialized);
        Assert.Equal(0, handler.Ran);
        Assert.Equal(0, host.Resolver.Calls);
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task WithAnUnreadableStateEveryBodyGetsTheUnavailableAnswer(string body)
    {
        operators.RequirePostgreSql();
        var handler = new Handler();
        await using var host = await operators.HostAsync(handler.Options(unreachable: true));

        using var response = await host.Client.SendAsync(Request(body, credential: null), Token);

        await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        Assert.Equal(0, handler.Ran);
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task WithoutACredentialOrAGrantEveryBodyIsRefusedByThePipeline(string body)
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var ungranted = await operators.PrincipalAsync(granted: false);
        var handler = new Handler();
        await using var host = await operators.HostAsync(handler.Options());

        using var anonymous = await host.Client.SendAsync(Request(body, credential: null), Token);
        using var denied = await host.Client.SendAsync(Request(body, ungranted.Value), Token);

        await AdminRouteTests.AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
        await AdminRouteTests.AssertErrorAsync(denied, HttpStatusCode.Forbidden, ErrorCodes.IdentityAuthDenied);
        Assert.Equal(0, handler.Ran);
    }

    [Fact]
    public async Task AnAdmittedRequestBindsTheBodyTheRouteValueAndTheQuery()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        var handler = new Handler();
        await using var host = await operators.HostAsync(handler.Options());

        using var response = await host.Client.SendAsync(Request("valid", principal.Value), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("item-7:3:bound", await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(1, handler.Ran);
    }

    // Binding still applies once the pipeline admits a request: the wrong media type is the delegate factory's 415, and a
    // malformed body its 400.
    [Theory]
    [InlineData("text", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    public async Task AnAdmittedRequestWithABodyBindingRefusesIsRefusedByBinding(string body, HttpStatusCode status)
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        var handler = new Handler();
        await using var host = await operators.HostAsync(handler.Options());

        using var response = await host.Client.SendAsync(Request(body, principal.Value), Token);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(0, handler.Ran);
    }

    private static HttpRequestMessage Request(string body, string? credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = body switch
            {
                "text" => new StringContent("bound", Encoding.UTF8, "text/plain"),
                "malformed" => new StringContent("{\"name\":", Encoding.UTF8, "application/json"),
                _ => JsonContent.Create(new Payload("bound")),
            },
        };
        if (credential is not null)
        {
            request.Headers.Add(TestOperatorPrincipalResolver.Header, credential);
        }

        return request;
    }

    public sealed record Payload(string Name);

    // A later spec's protected handler, mapped as a route handler so the delegate factory binds its parameters.
    private sealed class Handler
    {
        private int _ran;

        public int Ran => Volatile.Read(ref _ran);

        public OperatorHostOptions Options(bool unreachable = false) => new()
        {
            Unreachable = unreachable,
            Map = app => app.MapIskenderOperatorSecurity("/admin/v1")
                .MapPost("/bound/{item}", (Payload payload, string item, int count) =>
                {
                    Interlocked.Increment(ref _ran);
                    return $"{item}:{count}:{payload.Name}";
                })
                .RequireOperatorAction(OperatorActions.InstallationRead),
        };
    }
}

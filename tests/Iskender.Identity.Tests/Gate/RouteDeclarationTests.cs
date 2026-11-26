using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Iskender.Identity.Tests.Gate;

/// <summary>
/// The route-declaration check (ISK-0010 T020, operator-security contract Admin routes rule 2, research R9, FR-016): a
/// route in the group with no declaration or both, a second mapping call, and no mapping call each fail the start with
/// <c>operator.route_undeclared</c> and event 9005, and the server answers nothing; the shipped group passes.
/// </summary>
public class RouteDeclarationTests(OperatorFixture operators) : IClassFixture<OperatorFixture>
{
    private const string Prefix = "/admin/v1";

    public static TheoryData<string> Mistakes() => ["neither", "both", "second call", "no call"];

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task EachMistakeFailsTheStartWithRouteUndeclared(string mistake)
    {
        operators.RequirePostgreSql();
        var options = new OperatorHostOptions { Start = false, Map = Mapping(mistake) };
        await using var host = await operators.HostAsync(options);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => host.Host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.OperatorRouteUndeclared, failure.Code);
        var logged = host.Host.Logs.Only(9005);
        Assert.Equal(mistake switch { "neither" or "both" => Prefix + "/extra", "second call" => "/admin/v2", _ => OperatorRouteCheck.NoSingleGroup }, logged.Value("Route"));
    }

    [Fact]
    public async Task TheShippedGroupPasses()
    {
        operators.RequirePostgreSql();

        await using var host = await operators.HostAsync();

        Assert.Empty(host.Host.Logs.ById(9005));
    }

    private static Action<WebApplication> Mapping(string mistake) => app =>
    {
        switch (mistake)
        {
            case "neither":
                app.MapIskenderOperatorSecurity(Prefix).MapGet("/extra", () => "x");
                break;
            case "both":
                app.MapIskenderOperatorSecurity(Prefix).MapGet("/extra", () => "x")
                    .RequireOperatorAction(OperatorActions.InstallationRead).AllowAnonymousOperatorEndpoint("control-plane.traffic.extra");
                break;
            case "second call":
                app.MapIskenderOperatorSecurity(Prefix);
                app.MapIskenderOperatorSecurity("/admin/v2");
                break;
            default:
                app.MapGet("/other", (HttpContext _) => "x");
                break;
        }
    };
}

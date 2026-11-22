using System.Net;
using Iskender.Core.Contracts.Errors;
using Iskender.Identity.Tests.Fixture;
using Iskender.Identity.Tests.Gate;

namespace Iskender.Identity.Tests.Privilege;

/// <summary>
/// A host with no principal resolver (ISK-0010 T030, US3.5, FR-017): composed with the production registrations only, it
/// answers <c>401</c> <c>identity.unauthenticated</c> on the installation route of an initialized installation, whatever
/// headers it is sent, even the ID of a granted principal.
/// </summary>
public class NoResolverTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task EveryRequestIsUnauthenticatedWhateverItsHeaders()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var granted = await operators.PrincipalAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions { WithResolver = false });
        var headers = new (string Name, string Value)[]
        {
            (TestOperatorPrincipalResolver.Header, granted.Value),
            ("Authorization", "Bearer " + granted.Value),
            ("Authorization", "Basic b3BlcmF0b3I6b3BlcmF0b3I="),
            ("Cookie", "session=" + granted.Value),
            ("X-Forwarded-User", granted.Value),
        };

        using (var bare = await host.Client.GetAsync("/admin/v1/installation", Token))
        {
            await AdminRouteTests.AssertErrorAsync(bare, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
        }

        foreach (var (name, value) in headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/installation");
            request.Headers.TryAddWithoutValidation(name, value);

            using var response = await host.Client.SendAsync(request, Token);

            await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.IdentityUnauthenticated);
        }

        Assert.Equal(0, host.Resolver.Calls);
    }
}

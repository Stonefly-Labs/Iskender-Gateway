using System.Net;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Iskender.Identity.Tests.Gate;

/// <summary>
/// The uninitialized gate (ISK-0010 T021, US1.1, US1.4, FR-018, SC-001): on an empty migrated database, the protected
/// route answers <c>403</c> <c>operator.uninitialized</c> with no credential, a malformed one, and one the test resolver
/// accepts; a protected route a test adds answers the same with no code of its own; nothing in <c>isk_identity</c> or
/// <c>isk_audit</c> changes; and the resolver is never called, because the gate runs before authentication.
/// </summary>
public class UninitializedGateTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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

    public static TheoryData<string> Credentials() => ["none", "malformed", "accepted"];

    [Theory]
    [MemberData(nameof(Credentials))]
    public async Task EveryCredentialGetsTheUninitializedAnswerAndNothingChanges(string credential)
    {
        operators.RequirePostgreSql();
        var before = await operators.AllRowsAsync();
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Map = app => app.MapIskenderOperatorSecurity("/admin/v1")
                .MapGet("/added", (HttpContext _) => "added")
                .RequireOperatorAction(OperatorActions.InstallationRead),
        });

        foreach (var path in new[] { "/admin/v1/installation", "/admin/v1/added" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            var header = credential switch
            {
                "malformed" => "prin_not-a-valid-id",
                "accepted" => Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Principal).Value,
                _ => null,
            };
            if (header is not null)
            {
                request.Headers.Add(TestOperatorPrincipalResolver.Header, header);
            }

            using var response = await host.Client.SendAsync(request, Token);

            await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OperatorUninitialized);
        }

        Assert.Equal(0, host.Resolver.Calls);
        Assert.Equal(before, await operators.AllRowsAsync());
        Assert.All(host.Host.Logs.ById(9003), logged => Assert.Equal("uninitialized", logged.Value("Reason")));
    }
}

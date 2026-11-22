using System.Net;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Gate;

/// <summary>
/// An unreadable installation state (ISK-0010 T022, US1.2, FR-019, operator-security contract Installation state rule 4,
/// spec Edge Cases): a host whose store is unreachable answers <c>503</c> <c>operator.state_unavailable</c> and its handler
/// never runs; a replica that has read <c>initialized</c> keeps passing the gate when the store later fails; a replica
/// that read <c>uninitialized</c> reads again on the next request.
/// </summary>
public class UnreadableStateTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task AnUnreachableStoreAnswers503AndTheHandlerNeverRuns()
    {
        operators.RequirePostgreSql();
        var ran = 0;
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            Unreachable = true,
            Map = app => app.MapIskenderOperatorSecurity("/admin/v1")
                .MapGet("/added", (HttpContext _) => Interlocked.Increment(ref ran))
                .RequireOperatorAction(OperatorActions.InstallationRead),
        });

        foreach (var path in new[] { "/admin/v1/installation", "/admin/v1/added" })
        {
            using var response = await host.Client.GetAsync(path, Token);

            await AdminRouteTests.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        }

        Assert.Equal(0, ran);
        Assert.Equal(0, host.Resolver.Calls);
        Assert.All(host.Host.Logs.ById(9008), logged => Assert.Equal("operator.state_unavailable", logged.Value("Code")));
    }

    [Fact]
    public async Task AReplicaThatReadInitializedKeepsPassingTheGateWhenTheStoreLaterFails()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        var state = new SwitchableState(new PostgreSqlInstallationState(operators.Factory));
        await using var host = await operators.HostAsync(new OperatorHostOptions { Services = services => services.AddSingleton<IInstallationState>(state) });

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(host, principal.Value)).StatusCode);
        state.Fail = true;

        using var response = await GetAsync(host, principal.Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, state.Reads);
    }

    [Fact]
    public async Task AReplicaThatReadUninitializedReadsAgainOnTheNextRequest()
    {
        operators.RequirePostgreSql();
        var principal = await operators.PrincipalAsync();
        var state = new SwitchableState(new PostgreSqlInstallationState(operators.Factory));
        await using var host = await operators.HostAsync(new OperatorHostOptions { Services = services => services.AddSingleton<IInstallationState>(state) });

        await AdminRouteTests.AssertErrorAsync(await GetAsync(host, principal.Value), HttpStatusCode.Forbidden, ErrorCodes.OperatorUninitialized);
        state.Fail = true;
        await AdminRouteTests.AssertErrorAsync(await GetAsync(host, principal.Value), HttpStatusCode.ServiceUnavailable, ErrorCodes.OperatorStateUnavailable);
        state.Fail = false;
        await operators.InitializeInstallationAsync();

        using var response = await GetAsync(host, principal.Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, state.Reads);
    }

    private static Task<HttpResponseMessage> GetAsync(OperatorHost host, string principal)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/installation");
        request.Headers.Add(TestOperatorPrincipalResolver.Header, principal);
        return host.Client.SendAsync(request, Token);
    }

    /// <summary>The real store, which can be switched to fail as an unreachable one does.</summary>
    private sealed class SwitchableState(IInstallationState inner) : IInstallationState
    {
        private int _reads;

        public bool Fail { get; set; }

        public int Reads => Volatile.Read(ref _reads);

        public ValueTask<InstallationStatus> ReadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            return Fail ? throw new GatewayException(ErrorCodes.PersistenceUnavailable) : inner.ReadAsync(cancellationToken);
        }
    }
}

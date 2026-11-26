using System.Collections.Concurrent;
using System.Net;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Identity.Tests.Fixture;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Privilege;

/// <summary>
/// A scoped principal resolver, as ISK-0013, ISK-0014, or ISK-0020 may register (ISK-0010 T069, hosting contract,
/// Embedding rule 3, FR-017, AV-3; CONV-0010-011): the pipeline resolves it from each request's services, so it neither
/// fails scope validation nor becomes a captive of the singleton pipeline, and each request gets its own.
/// </summary>
public class ScopedResolverTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
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
    public async Task AScopedResolverIsResolvedOncePerRequestFromItsScope()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var principal = await operators.PrincipalAsync();
        var seen = new ConcurrentBag<ScopedResolver>();
        await using var host = await operators.HostAsync(new OperatorHostOptions
        {
            WithResolver = false,
            Services = services =>
            {
                services.AddSingleton(seen);
                services.AddScoped<RequestScoped>();
                services.AddScoped<IOperatorPrincipalResolver, ScopedResolver>();
            },
        });

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/v1/installation");
            request.Headers.Add(TestOperatorPrincipalResolver.Header, principal.Value);
            using var response = await host.Client.SendAsync(request, Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, seen.Count);
        Assert.Equal(2, seen.Distinct().Count());
        Assert.All(seen, resolver => Assert.True(resolver.Scoped.Disposed));
    }

    /// <summary>A scoped dependency, disposed with its request's scope.</summary>
    private sealed class RequestScoped : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Resolves the header's principal, as the test resolver does, and records each instance it is.</summary>
    private sealed class ScopedResolver(RequestScoped scoped, ConcurrentBag<ScopedResolver> seen) : IOperatorPrincipalResolver
    {
        public RequestScoped Scoped { get; } = scoped;

        public ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
        {
            seen.Add(this);
            return ValueTask.FromResult(
                ResourceId.TryParse(context.Request.Headers[TestOperatorPrincipalResolver.Header].ToString(), ResourceTypes.Principal, out var principal, out _)
                    ? OperatorPrincipalResolution.Resolved(principal!)
                    : OperatorPrincipalResolution.Unresolved);
        }
    }
}

using System.Net;
using System.Runtime.CompilerServices;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Requests;
using Iskender.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>The spec's Edge Cases on routing (hosting contract, Route group rules 1 and 3, and Coexistence rules 3 and 4).</summary>
public class CoexistenceEdgeCaseTests
{
    private const string Hello = "hello from the host";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGroupAtTheRootKeepsTheHostsRouteAndAnswersEveryOtherPath()
    {
        await using var host = await HostAsync(application =>
        {
            application.MapGet("/hello", () => Hello);
            application.MapIskender("/");
        });

        Assert.Equal(Hello, await host.Client.GetStringAsync(new Uri("/hello", UriKind.Relative), Token));
        await AssertEnvelopeAsync(host, "/anything/else");
        await AssertEnvelopeAsync(host, "/");
    }

    [Fact]
    public async Task AHostRouteUnderTheIskenderPrefixWinsOverTheCatchAll()
    {
        await using var host = await HostAsync(application =>
        {
            application.MapIskender("/iskender");
            application.MapGet("/iskender/mine", () => Hello);
        });

        Assert.Equal(Hello, await host.Client.GetStringAsync(new Uri("/iskender/mine", UriKind.Relative), Token));
        await AssertEnvelopeAsync(host, "/iskender/other");
    }

    [Fact]
    public async Task TwoGroupsServeBothPrefixesWithOneSnapshotAndOneRevision()
    {
        await using var host = await HostAsync(application =>
        {
            application.MapIskender("/first").MapGet("/revision", (HttpContext http) => Revision(http.RequestServices.GetRequiredService<ConfigurationSnapshot>(), Context(http)));
            application.MapIskender("/second").MapGet("/revision", (HttpContext http) => Revision(http.RequestServices.GetRequiredService<ConfigurationSnapshot>(), Context(http)));
        });

        var first = await host.Client.GetStringAsync(new Uri("/first/revision", UriKind.Relative), Token);
        var second = await host.Client.GetStringAsync(new Uri("/second/revision", UriKind.Relative), Token);

        Assert.Equal(first, second);
        Assert.Equal(Revision(host.Services.GetRequiredService<ConfigurationSnapshot>(), host.Services.GetRequiredService<ConfigurationSnapshot>().Revision), first);
        await AssertEnvelopeAsync(host, "/first/missing");
        await AssertEnvelopeAsync(host, "/second/missing");
    }

    [Theory]
    [InlineData("caller-value_1.2:3", true)]
    [InlineData("has space", false)]
    public async Task ACallersCorrelationValueIsKeptOrDroppedAndTheGatewayIdStaysAuthoritative(string value, bool kept)
    {
        await using var host = await HostAsync(application => application.MapIskender("/iskender")
            .MapGet("/correlation", (HttpContext http) => $"{Context(http).Correlation.CallerValue}|{Context(http).Correlation.CallerValueDropped}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/iskender/correlation");
        request.Headers.TryAddWithoutValidation(RequestBootstrap.CorrelationHeader, value);
        using var response = await host.Client.SendAsync(request, Token);
        using var missing = new HttpRequestMessage(HttpMethod.Get, "/iskender/missing");
        missing.Headers.TryAddWithoutValidation(RequestBootstrap.CorrelationHeader, value);
        using var envelopeResponse = await host.Client.SendAsync(missing, Token);

        Assert.Equal(kept ? $"{value}|False" : "|True", await response.Content.ReadAsStringAsync(Token));
        var envelope = ErrorEnvelopeCodec.Read(await envelopeResponse.Content.ReadAsByteArrayAsync(Token));
        Assert.Matches("^req_[0-9a-hjkmnp-tv-z]{26}$", envelope.CorrelationId);
        Assert.NotEqual(value, envelope.CorrelationId);
    }

    [Fact]
    public async Task HostMiddlewareRegisteredBeforeTheEndpointsRunsForIskendersRequests()
    {
        await using var host = await HostAsync(application =>
        {
            application.Use((context, next) =>
            {
                context.Response.Headers["X-Host-Middleware"] = "ran";
                return next(context);
            });
            application.MapIskender("/iskender");
        });

        using var response = await host.Client.GetAsync(new Uri("/iskender/anything", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(["ran"], response.Headers.GetValues("X-Host-Middleware"));
    }

    // Minimal APIs bind a handler's service parameters before its filters run, so a group endpoint resolves the request's
    // context inside the handler, after the bootstrap.
    private static GatewayExecutionContext Context(HttpContext http) => http.RequestServices.GetRequiredService<GatewayExecutionContext>();

    // The request's revision, and the identity of the snapshot that served it.
    private static string Revision(ConfigurationSnapshot snapshot, GatewayExecutionContext context) => Revision(snapshot, context.ConfigurationRevision);

    private static string Revision(ConfigurationSnapshot snapshot, ConfigurationRevision revision) =>
        $"{revision.Value}|{RuntimeHelpers.GetHashCode(snapshot)}|{revision == snapshot.Revision}";

    private static Task<EmbeddedHost> HostAsync(Action<WebApplication> map) =>
        EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(_ => { }), map, ct: Token);

    private static async Task AssertEnvelopeAsync(EmbeddedHost host, string path)
    {
        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        Assert.Equal(ErrorCodes.ResourceNotFound.Id, ErrorEnvelopeCodec.Read(await response.Content.ReadAsByteArrayAsync(Token)).Code);
    }
}

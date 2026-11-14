using System.Net;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Requests;

/// <summary><c>MapIskender(...)</c>'s arguments, its registration check, and its group (hosting contract, Route group rules 1 and 5).</summary>
public class MapIskenderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("iskender")]
    [InlineData(" /iskender")]
    public async Task APrefixThatIsEmptyOrDoesNotStartWithASlashIsInvalid(string? prefix)
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskender(_ => { }),
            application => application.MapIskender(prefix!),
            ct: Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
    }

    [Fact]
    public void ANullRouteBuilderIsInvalid()
    {
        var failure = Assert.Throws<GatewayException>(() => ((IEndpointRouteBuilder)null!).MapIskender("/iskender"));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
    }

    [Fact]
    public async Task WithoutAddIskenderItWrites6003AndFailsWithRegistrationMissing()
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Logging.AddProvider(logs),
            application => application.MapIskender("/iskender"),
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("missing", "hosting"), failure.Error!.Diagnostics!.Values);
        Assert.Contains(new KeyValuePair<string, string>("requiredBy", "MapIskender"), failure.Error.Diagnostics.Values);
        var entry = logs.Only(6003);
        Assert.Equal("Iskender.Hosting", entry.Category);
        Assert.Equal("hosting", entry.Value("Missing"));
        Assert.Equal("MapIskender", entry.Value("RequiredBy"));
    }

    [Fact]
    public async Task ItReturnsTheGroupForTheHostsConventions()
    {
        RouteGroupBuilder? group = null;

        await using var host = await EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskender(_ => { }),
            application => group = application.MapIskender("/iskender"),
            ct: Token);

        Assert.NotNull(group);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task TheCatchAllAnswersEveryMethodWithTheNotFoundEnvelope(string method)
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(_ => { }), application => application.MapIskender("/iskender"), ct: Token);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/iskender/any/path");

        using var response = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        var envelope = ErrorEnvelopeCodec.Read(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal("core.resource.not_found", envelope.Code);
        Assert.Matches("^req_[0-9a-hjkmnp-tv-z]{26}$", envelope.CorrelationId);
    }
}

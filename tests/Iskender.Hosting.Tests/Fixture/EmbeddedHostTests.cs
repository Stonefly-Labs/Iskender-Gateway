using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Iskender.Hosting.Tests.Fixture;

/// <summary>The fixture builds and starts an embedded host in process, with no socket (research R17).</summary>
public class EmbeddedHostTests
{
    private static readonly IskenderConfigurationDocument Base =
        IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"20s\"}}}");

    [Fact]
    public async Task AHostWithAddIskenderStartsAndLogs3001And6001NamingOneRevision()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Base), ct: ct);

        var snapshot = Assert.IsType<ConfigurationSnapshot>(host.Services.GetService(typeof(ConfigurationSnapshot)), exactMatch: false);
        var validated = host.Logs.Only(3001);
        var composed = host.Logs.Only(6001);
        Assert.Equal("Iskender.Configuration", validated.Category);
        Assert.Equal("Iskender.Hosting", composed.Category);
        Assert.Equal(snapshot.Revision.Value, validated.Value("Revision"));
        Assert.Equal(snapshot.Revision.Value, composed.Value("Revision"));
        Assert.Equal("hosting", composed.Value("Modules"));
        Assert.Equal("hosting@1", composed.Value("Sections"));
        Assert.Equal("(none)", composed.Value("Environment"));
        Assert.IsType<TestServer>(host.Server);
    }

    [Fact]
    public async Task AHostThatIsNotStartedHasValidatedNothing()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Base), start: false, ct: TestContext.Current.CancellationToken);

        Assert.Empty(host.Logs.ById(3001));
        Assert.Empty(host.Logs.ById(6001));
    }

    [Fact]
    public async Task AFailedStartThrowsTheHostsGatewayException()
    {
        var rejected = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"0s\"}}}");

        var failure = await Assert.ThrowsAsync<GatewayException>(() =>
            EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = rejected), ct: TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
    }
}

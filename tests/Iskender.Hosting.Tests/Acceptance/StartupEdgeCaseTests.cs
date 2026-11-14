using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Configuration;
using Iskender.Hosting.Testing;
using Iskender.Hosting.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>The spec's Edge Cases on configuration (hosting contract, Start-up rules 1 to 4 and 9).</summary>
public class StartupEdgeCaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithNoSourceAtAllTheDefaultsValidate()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(_ => { }), ct: Token);

        Assert.Equal(TimeSpan.FromSeconds(100), host.Services.GetRequiredService<HostingSettings>().RequestTimeout);
    }

    [Fact]
    public async Task WithNoSourceARequiredFieldRejectsTheDefaults()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(_ => { });
                builder.Services.AddTestSchema();
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.field.required", failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBaseFileFailsStartNamingItsLayerAndSourceOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "iskender-missing-" + Guid.NewGuid().ToString("N"), "base.yaml");

        await AssertUnreadableAsync(options => options.BaseDocument = IskenderConfigurationDocument.FromFile(path, IskenderDocumentFormat.Yaml, "missing-base"), "base", "missing-base", path);
    }

    [Fact]
    public async Task AThrowingOverlayFactoryFailsStartNamingItsLayerAndSourceOnly()
    {
        const string Thrown = "overlay-factory-exception-text";

        await AssertUnreadableAsync(
            options => options.OverlayDocument = IskenderConfigurationDocument.FromStream("overlay-stream", IskenderDocumentFormat.Json, () => throw new IOException(Thrown)),
            "overlay",
            "overlay-stream",
            Thrown);
    }

    [Fact]
    public async Task AnInvalidEnvironmentNameFailsStart()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.EnvironmentName = "Staging_EU"), ct: Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.environment.invalid", failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHostsOwnIskenderKeyIsNeitherReadNorChanged()
    {
        await using var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Configuration["iskender:hosting:requestTimeout"] = "7s";
                builder.Services.AddIskender(_ => { });
            },
            ct: Token);

        Assert.Equal(TimeSpan.FromSeconds(100), host.Services.GetRequiredService<HostingSettings>().RequestTimeout);
        Assert.Equal("7s", host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["iskender:hosting:requestTimeout"]);
    }

    [Fact]
    public void OneCollectionBuiltIntoTwoContainersValidatesTwice()
    {
        var opens = 0;
        var services = new ServiceCollection();
        services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromStream("counted.json", IskenderDocumentFormat.Json, () =>
        {
            Interlocked.Increment(ref opens);
            return new MemoryStream("{\"iskender\":{\"hosting\":{\"requestTimeout\":\"20s\"}}}"u8.ToArray());
        }));
        using var first = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var second = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var one = first.GetRequiredService<ConfigurationSnapshot>();
        var two = second.GetRequiredService<ConfigurationSnapshot>();

        Assert.NotSame(one, two);
        Assert.Equal(one.Revision, two.Revision);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task AStopDuringABlockedValidationStoresNothingAndLeavesNothingRunning()
    {
        using var opened = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Blocking(opened, release, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"20s\"}}}")), start: false, ct: Token);
        using var stopping = new CancellationTokenSource();

        var start = Task.Run(() => host.StartAsync(stopping.Token), Token);
        await opened.WaitAsync(Token);
        await stopping.CancelAsync();
        release.Release();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Empty(host.Logs.ById(3001));
        Assert.Empty(host.Logs.ById(6001));

        // Nothing was stored: a later need validates afresh, and reads the document again.
        release.Release();
        Assert.Equal(TimeSpan.FromSeconds(20), host.Services.GetRequiredService<HostingSettings>().RequestTimeout);
        Assert.Single(host.Logs.ById(3001));
    }

    /// <summary>
    /// The in-memory server takes requests only once the host has started, after validation, so a request's own need for
    /// the snapshot is shown by resolving it while the start's validation is blocked: it waits, and then fails with the
    /// start's rejection, and never sees a default configuration.
    /// </summary>
    [Fact]
    public async Task ANeedForTheSnapshotDuringABlockedValidationWaitsAndNeverSeesDefaults()
    {
        using var opened = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Blocking(opened, release, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"0s\"}}}")), start: false, ct: Token);

        var start = Task.Run(() => host.StartAsync(Token), Token);
        await opened.WaitAsync(Token);
        var need = Task.Run(() => host.Services.GetRequiredService<ConfigurationSnapshot>(), Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Token);
        Assert.False(need.IsCompleted);
        release.Release();

        Assert.Same(ErrorCodes.HostingConfigurationRejected, (await Assert.ThrowsAsync<GatewayException>(() => start)).Code);
        Assert.Same(ErrorCodes.HostingConfigurationRejected, (await Assert.ThrowsAsync<GatewayException>(() => need)).Code);
    }

    private static async Task AssertUnreadableAsync(Action<IskenderOptions> configure, string layer, string source, string absent)
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                builder.Services.AddIskender(configure);
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        var diagnostics = failure.Error!.Diagnostics!;
        Assert.Contains(new KeyValuePair<string, string>("layer", layer), diagnostics.Values);
        Assert.Contains(new KeyValuePair<string, string>("source", source), diagnostics.Values);
        Assert.Contains(new KeyValuePair<string, string>("errorCount", "1"), diagnostics.Values);
        Assert.Contains(new KeyValuePair<string, string>("reportedCount", "1"), diagnostics.Values);
        Assert.DoesNotContain(absent, diagnostics.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(absent, failure.Message, StringComparison.Ordinal);
        Assert.Equal("1", logs.Only(6002).Value("ErrorCount"));
        Assert.DoesNotContain(logs.Entries, entry => entry.Event.Id is >= 3001 and <= 3003);
    }

    // A stream factory that signals it was called, then waits for one release before answering with the text.
    private static IskenderConfigurationDocument Blocking(SemaphoreSlim opened, SemaphoreSlim release, string text) =>
        IskenderConfigurationDocument.FromStream("blocking.json", IskenderDocumentFormat.Json, () =>
        {
            opened.Release();
            release.Wait();
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        });
}

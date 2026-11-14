using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Hosting.Composition;
using Iskender.Hosting.Configuration;

namespace Iskender.Hosting.Tests.Configuration;

/// <summary>Validation once per container, its stored outcome, and its sources, with no host (research R6, R7, R15).</summary>
public class IskenderConfigurationTests
{
    private const string Valid = "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"30s\"}}}";

    private const string Invalid = "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"0s\",\"unknown\":1}}}";

    private static readonly ConfigurationSchemaRegistry Registry = ConfigurationSchemaRegistry.Create([HostingSectionSchema.Schema]);

    private static IskenderConfiguration Create(
        IskenderConfigurationDocument? baseDocument = null,
        IskenderConfigurationDocument? overlayDocument = null,
        bool readEnvironmentVariables = false,
        string? environmentName = null) =>
        new(new IskenderRuntimeOptions(baseDocument, overlayDocument, readEnvironmentVariables, environmentName), Registry, loggerFactory: null);

    private static IskenderConfigurationDocument Counting(string text, Action? onOpen = null) =>
        IskenderConfigurationDocument.FromStream("counted.json", IskenderDocumentFormat.Json, () =>
        {
            onOpen?.Invoke();
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        });

    private static string Value(GatewayException exception, string key) =>
        Assert.Single(exception.Error!.Diagnostics!.Values, pair => pair.Key == key).Value;

    [Fact]
    public async Task SixteenConcurrentCallersValidateOnceAndShareOneSnapshot()
    {
        var opens = 0;
        using var configuration = Create(Counting(Valid, () => Interlocked.Increment(ref opens)));
        using var start = new ManualResetEventSlim();

        var callers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            return configuration.Snapshot;
        })).ToList();
        start.Set();
        var snapshots = await Task.WhenAll(callers);

        Assert.Equal(1, opens);
        Assert.All(snapshots, snapshot => Assert.Same(snapshots[0], snapshot));
        Assert.Same(snapshots[0].Revision, configuration.Revision);
        Assert.Equal(TimeSpan.FromSeconds(30), HostingSettings.From(configuration.Snapshot).RequestTimeout);
    }

    [Fact]
    public void ARejectedConfigurationIsStoredAndRethrownWithTheReport()
    {
        var opens = 0;
        using var configuration = Create(Counting(Invalid, () => opens++));

        var first = Assert.Throws<GatewayException>(() => configuration.Snapshot);
        var second = Assert.Throws<GatewayException>(() => configuration.Validate(TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, first.Code);
        Assert.Same(first, second);
        Assert.Equal(1, opens);
        Assert.Equal("2", Value(first, "errorCount"));
        Assert.Equal("2", Value(first, "reportedCount"));
        Assert.Contains("config.field.unknown", first.Error!.Diagnostics!.Text, StringComparison.Ordinal);
        Assert.Contains("config.field.out_of_range", first.Error.Diagnostics.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatCannotBeReadIsRejectedNamingTheLayerAndSourceOnly()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"iskender-{Guid.NewGuid():N}", "absent.yaml");
        using var configuration = Create(IskenderConfigurationDocument.FromFile(missing, IskenderDocumentFormat.Yaml, "base-file"));

        var failure = Assert.Throws<GatewayException>(() => configuration.Snapshot);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Equal("The base configuration document \"base-file\" could not be read.", failure.Error!.Diagnostics!.Text);
        Assert.Equal("base", Value(failure, "layer"));
        Assert.Equal("base-file", Value(failure, "source"));
        Assert.Equal("1", Value(failure, "errorCount"));
        Assert.Equal("1", Value(failure, "reportedCount"));
        Assert.DoesNotContain(missing, failure.Error.Diagnostics.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AStreamFactoryThatThrowsIsRejectedAsTheOverlayWithNoExceptionText()
    {
        using var configuration = Create(
            IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, Valid),
            IskenderConfigurationDocument.FromStream("overlay-stream", IskenderDocumentFormat.Json, () => throw new IOException("secret-path")));

        var failure = Assert.Throws<GatewayException>(() => configuration.Snapshot);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Equal("The overlay configuration document \"overlay-stream\" could not be read.", failure.Error!.Diagnostics!.Text);
        Assert.Equal("overlay", Value(failure, "layer"));
        Assert.DoesNotContain(failure.Error.Diagnostics.Values, pair => pair.Value.Contains("secret-path", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStreamIsDisposedAfterItIsRead()
    {
        var stream = new TrackingStream(System.Text.Encoding.UTF8.GetBytes(Valid));
        using var configuration = Create(IskenderConfigurationDocument.FromStream("tracked.json", IskenderDocumentFormat.Json, () => stream));

        _ = configuration.Snapshot;

        Assert.True(stream.Disposed);
    }

    [Fact]
    public void ACancelledRunStoresNothingAndTheNextCallerStartsOver()
    {
        using var cancellation = new CancellationTokenSource();
        var opens = 0;
        using var configuration = Create(Counting(Valid, () =>
        {
            if (++opens == 1)
            {
                cancellation.Cancel();
            }
        }));

        Assert.ThrowsAny<OperationCanceledException>(() => configuration.Validate(cancellation.Token));
        var snapshot = configuration.Validate(TestContext.Current.CancellationToken);

        Assert.Equal(2, opens);
        Assert.Same(snapshot, configuration.Snapshot);
    }

    [Fact]
    public void ProcessVariablesAreIgnoredWithoutTheOptInAndAppliedWithIt()
    {
        const string Name = "ISKENDER__HOSTING__REQUESTTIMEOUT";
        Environment.SetEnvironmentVariable(Name, "7s");
        try
        {
            using var without = Create();
            using var with = Create(readEnvironmentVariables: true);

            Assert.Equal(0, without.Snapshot.Layers.EnvironmentVariableCount);
            Assert.Equal(TimeSpan.FromSeconds(100), HostingSettings.From(without.Snapshot).RequestTimeout);
            Assert.Equal(1, with.Snapshot.Layers.EnvironmentVariableCount);
            Assert.Equal(TimeSpan.FromSeconds(7), HostingSettings.From(with.Snapshot).RequestTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, null);
        }
    }

    [Theory]
    [InlineData("staging")]
    [InlineData(null)]
    public void TheEnvironmentNameIsPassedThroughUnchanged(string? name)
    {
        using var configuration = Create(environmentName: name);

        Assert.Equal(name, configuration.Snapshot.EnvironmentName);
    }

    [Fact]
    public void NoSourceAtAllValidatesOnDefaults()
    {
        using var configuration = Create();

        var snapshot = configuration.Snapshot;

        Assert.Null(snapshot.Layers.BaseSource);
        Assert.Null(snapshot.Layers.OverlaySource);
        Assert.Equal(TimeSpan.FromSeconds(100), HostingSettings.From(snapshot).RequestTimeout);
    }

    [Fact]
    public void EachLayerKeepsItsSourceName()
    {
        using var configuration = Create(
            IskenderConfigurationDocument.FromText("base.yaml", IskenderDocumentFormat.Yaml, "iskender:\n  hosting:\n    requestTimeout: 10s\n"),
            IskenderConfigurationDocument.FromText("overlay.json", IskenderDocumentFormat.Json, Valid));

        var snapshot = configuration.Snapshot;

        Assert.Equal("base.yaml", snapshot.Layers.BaseSource);
        Assert.Equal("overlay.json", snapshot.Layers.OverlaySource);
        Assert.Equal(TimeSpan.FromSeconds(30), HostingSettings.From(snapshot).RequestTimeout);
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

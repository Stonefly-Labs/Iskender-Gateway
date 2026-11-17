using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Domain.Tests.Configuration;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary><see cref="CompositionConfiguration"/>'s states, and its logger-free probe (research R1, R13).</summary>
public class CompositionConfigurationTests
{
    private const string Valid = "{\"iskender\":{\"alpha\":{\"count\":3}}}";

    private const string Invalid = "{\"iskender\":{\"alpha\":{\"count\":\"three\"}}}";

    private static readonly ConfigurationSchemaRegistry Registry = ConfigurationSchemaRegistry.Create([TestSchemas.Alpha]);

    private static CompositionSources Sources(string text) =>
        new(CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, text), null, false, null);

    [Fact]
    public void AValidConfigurationIsStoredAndReturnedEveryTime()
    {
        var logs = new CapturingLoggerFactory();
        using var configuration = new CompositionConfiguration(Sources(Valid), Registry, logs);

        var snapshot = configuration.Validate(TestContext.Current.CancellationToken);

        Assert.Same(snapshot, configuration.Snapshot);
        Assert.Same(snapshot.Revision, configuration.Revision);
        Assert.Equal([3001], logs.Logger.Events.Select(entry => entry.Id.Id));
        Assert.Equal(CompositionConfiguration.LoggerCategory, Assert.Single(logs.Categories));
    }

    [Fact]
    public void ARejectedConfigurationFailsTheSameWayEveryTime()
    {
        var logs = new CapturingLoggerFactory();
        using var configuration = new CompositionConfiguration(Sources(Invalid), Registry, logs);

        var first = Assert.Throws<GatewayException>(() => configuration.Validate(TestContext.Current.CancellationToken));
        var written = logs.Logger.Events.Count;
        var second = Assert.Throws<GatewayException>(() => configuration.Snapshot);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, first.Code);
        Assert.Same(first, second);
        Assert.Contains(logs.Logger.Events, entry => entry.Id.Id == 3003);
        Assert.Equal(written, logs.Logger.Events.Count);
    }

    [Fact]
    public void ACancelledRunStoresNothing()
    {
        using var configuration = new CompositionConfiguration(Sources(Valid), Registry, loggerFactory: null);

        Assert.ThrowsAny<OperationCanceledException>(() => configuration.Validate(new CancellationToken(canceled: true)));

        Assert.NotNull(configuration.Validate(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AnUnreadableDocumentNamesItsLayerAndSourceOnly()
    {
        var sources = new CompositionSources(null, CompositionDocument.FromStream("overlay.json", ConfigurationDocumentFormat.Json, () => throw new IOException("secret detail")), false, null);
        using var configuration = new CompositionConfiguration(sources, Registry, loggerFactory: null);

        var failure = Assert.Throws<GatewayException>(() => configuration.Snapshot);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Equal(
            [new("errorCount", "1"), new("reportedCount", "1"), new("layer", "overlay"), new("source", "overlay.json")],
            failure.Error!.Diagnostics!.Values);
        Assert.DoesNotContain("secret detail", failure.Error.Diagnostics.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeReturnsTheSnapshotOrNullAndWritesNoEvent()
    {
        Assert.NotNull(CompositionConfiguration.Probe(Sources(Valid), Registry));
        Assert.Null(CompositionConfiguration.Probe(Sources(Invalid), Registry));
    }

    [Fact]
    public void DocumentsOpenNothingWhenDescribed()
    {
        var opened = 0;

        var document = CompositionDocument.FromStream("s.json", ConfigurationDocumentFormat.Json, () =>
        {
            opened++;
            return new MemoryStream();
        });
        _ = CompositionDocument.FromFile(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.yaml"), ConfigurationDocumentFormat.Yaml);

        Assert.Equal(0, opened);
        Assert.Equal(("s.json", ConfigurationDocumentFormat.Json), (document.SourceName, document.Format));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("a\u2028b")]
    public void ASourceNameOutsideTheRuleFails(string name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CompositionDocument.FromText(name, ConfigurationDocumentFormat.Json, "{}")).Code);
    }
}

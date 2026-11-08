using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>SC-004: an alias bomb is rejected before anything expands, with bounded allocation (research R4).</summary>
public class AliasBombTests
{
    private const long AllocationCeiling = 1_048_576;
    private const long ScalingTolerance = 65_536;

    private static long Allocated(ConfigurationDocumentSource source, out DocumentRead read)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        read = YamlDocumentReader.Read(source, ConfigurationLayer.Base);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Theory]
    [InlineData("invalid/yaml-alias-bomb.yaml")]
    [InlineData("invalid/yaml-alias-bomb-large.yaml")]
    public void EachBombStopsAtItsFirstAnchorBeforeAnyAlias(string fixture)
    {
        var report = FixtureLoader.AssertMatchesSidecar(fixture);
        var error = Assert.Single(report.Errors);

        var text = File.ReadAllText(FixtureLoader.FullPath(fixture));
        var firstAliasLine = text[..text.IndexOf('*', StringComparison.Ordinal)].Count(c => c == '\n') + 1;
        Assert.True(error.Location.Line < firstAliasLine);
    }

    [Fact]
    public void ReadingEitherBombAllocatesUnderTheCeilingAndTheLargerBombAllocatesNoMore()
    {
        // The warm-up rejects a different file the same way, so the error path is compiled before either measurement.
        var warmUp = FixtureLoader.Document("invalid/config.document.unsupported_construct/anchor.yaml");
        var bomb = FixtureLoader.Document("invalid/yaml-alias-bomb.yaml");
        var large = FixtureLoader.Document("invalid/yaml-alias-bomb-large.yaml");
        Allocated(warmUp, out _);

        var bombBytes = Allocated(bomb, out var bombRead);
        var largeBytes = Allocated(large, out var largeRead);
        TestContext.Current.TestOutputHelper?.WriteLine($"yaml-alias-bomb.yaml: {bombBytes} bytes allocated; yaml-alias-bomb-large.yaml: {largeBytes} bytes allocated");

        Assert.Equal(ErrorCodes.ConfigDocumentUnsupportedConstruct, bombRead.Error?.Code);
        Assert.Equal(ErrorCodes.ConfigDocumentUnsupportedConstruct, largeRead.Error?.Code);
        Assert.InRange(bombBytes, 0, AllocationCeiling - 1);
        Assert.InRange(largeBytes, 0, AllocationCeiling - 1);
        Assert.InRange(Math.Abs(largeBytes - bombBytes), 0, ScalingTolerance);
    }

    [Fact]
    public void Scenario6AStreamOverTheLimitIsRejectedAfterReadingAtMostOneByteMore()
    {
        var stream = new CountingStream((byte)'a');

        var source = ConfigurationDocumentSource.FromStream("big.yaml", ConfigurationDocumentFormat.Yaml, stream);
        var error = Assert.Single(ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), source)).Errors);

        Assert.Equal("config.document.too_large", error.Code.Id);
        Assert.InRange(stream.BytesRead, ConfigurationLimits.MaxDocumentBytes + 1, ConfigurationLimits.MaxDocumentBytes + 1);
    }
}

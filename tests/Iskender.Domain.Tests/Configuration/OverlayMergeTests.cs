using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 4 and FR-014: base, overlay, and environment merge in one fixed order.</summary>
public class OverlayMergeTests
{
    private const string Base = "iskender:\n  persistence:\n    host: base.example\n    connectionTimeout: 10\n    sampleRatio: 0.1\n    pool:\n      name: main\n    tags: [a, b, c]\n";

    private static ConfigurationDocumentSource Yaml(string name, string text) =>
        ConfigurationDocumentSource.FromString(name, ConfigurationDocumentFormat.Yaml, text);

    private static ConfigurationValidationReport Validate(string? overlay, params (string Name, string Value)[] variables) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            Yaml("base.yaml", Base),
            overlay is null ? null : Yaml("overlay.yaml", overlay),
            variables.Length == 0 ? null : ConfigurationEnvironmentSource.FromDictionary(variables.Select(v => KeyValuePair.Create(v.Name, v.Value)))));

    private static ConfigurationSection Persistence(ConfigurationValidationReport report)
    {
        Assert.True(report.IsValid, report.ToString());
        return report.Snapshot!.GetSection(TestSchemas.Persistence);
    }

    [Fact]
    public void Scenario1TheEnvironmentWinsThenTheOverlayThenTheBaseThenTheDefault()
    {
        var section = Persistence(Validate(
            "iskender:\n  persistence:\n    host: overlay.example\n    connectionTimeout: 20\n",
            ("ISKENDER__PERSISTENCE__HOST", "env.example")));

        Assert.Equal("env.example", section.GetString("host"));
        Assert.Equal(20, section.GetInt64("connectionTimeout"));
        Assert.Equal(0.1m, section.GetDecimal("sampleRatio"));
        Assert.Equal(TimeSpan.FromMinutes(5), section.GetDuration("idleTimeout"));
    }

    [Fact]
    public void Scenario2AnOverlayArrayReplacesTheBaseArrayWhole()
    {
        Assert.Equal(["x"], Persistence(Validate("iskender:\n  persistence:\n    tags: [x]\n")).GetStrings("tags")!);
    }

    [Fact]
    public void ObjectsMergeByKeyAtEveryLevel()
    {
        var section = Persistence(Validate("iskender:\n  persistence:\n    pool:\n      size: 50\n", ("ISKENDER__ALPHA__COUNT", "3")));

        Assert.Equal("main", section.GetSection("pool").GetString("name"));
        Assert.Equal(50, section.GetSection("pool").GetInt64("size"));
    }

    [Theory]
    [InlineData("iskender:\n  persistence:\n    hots: x\n", "iskender.persistence.hots", ConfigurationLayer.Overlay)]
    [InlineData("iskender:\n  gamma: {}\n", "iskender.gamma", ConfigurationLayer.Overlay)]
    public void Scenario3AnUnknownNameInTheOverlayNamesTheOverlay(string overlay, string path, ConfigurationLayer layer)
    {
        var error = Assert.Single(Validate(overlay).Errors);

        Assert.Equal(path, error.Path);
        Assert.Equal(layer, error.Location.Layer);
        Assert.Equal("overlay.yaml", error.Location.Source);
    }

    [Fact]
    public void Scenario3AnUnknownNameInTheEnvironmentNamesTheEnvironment()
    {
        var error = Assert.Single(Validate(null, ("ISKENDER__PERSISTENCE__HOTS", "x")).Errors);

        Assert.Equal("config.field.unknown", error.Code.Id);
        Assert.Equal(ConfigurationLayer.Environment, error.Location.Layer);
    }

    [Fact]
    public void NullInTheOverlayNeverRemovesABaseValue()
    {
        var section = Persistence(Validate("iskender:\n  persistence:\n    host: null\n    connectionTimeout: ~\n    tags:\n    pool: null\n"));

        Assert.Equal("base.example", section.GetString("host"));
        Assert.Equal(10, section.GetInt64("connectionTimeout"));
        Assert.Equal(["a", "b", "c"], section.GetStrings("tags")!);
        Assert.Equal("main", section.GetSection("pool").GetString("name"));
    }

    [Fact]
    public void AnOverlayRootOfNullOrAnEmptyIskenderChangesNothing()
    {
        var bare = Validate(null).Snapshot!.Revision;

        Assert.Equal(bare, Validate("iskender:\n").Snapshot!.Revision);
        Assert.Equal(bare, Validate("").Snapshot!.Revision);
        Assert.Equal(bare, Validate("iskender: {}\n").Snapshot!.Revision);
    }

    [Fact]
    public void AMergedValueKeepsTheHigherLayersLocation()
    {
        var error = Assert.Single(Validate("iskender:\n  persistence:\n    connectionTimeout: 0\n").Errors);

        Assert.Equal("config.field.out_of_range", error.Code.Id);
        Assert.Equal(new ConfigurationSourceLocation("overlay.yaml", 3, 24, ConfigurationLayer.Overlay), error.Location);

        var fromVariable = Assert.Single(Validate(null, ("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT", "0")).Errors);
        Assert.Equal(new ConfigurationSourceLocation("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT", 0, 0, ConfigurationLayer.Environment), fromVariable.Location);
    }

    [Fact]
    public void AMissingOverlayIsValidAndTheSnapshotRecordsTheLayersUsed()
    {
        var bare = Validate(null);
        var full = Validate("iskender: {}\n", ("ISKENDER__PERSISTENCE__HOST", "env.example"), ("ISKENDER__ALPHA__NAME", "x"), ("OTHER", "y"));
        var onlyVariables = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), environment: ConfigurationEnvironmentSource.FromDictionary(
            [KeyValuePair.Create("ISKENDER__PERSISTENCE__HOST", "db"), KeyValuePair.Create("ISKENDER__PERSISTENCE__POOL__NAME", "main")])));

        Assert.Equal(new ConfigurationLayers("base.yaml", null, 0), bare.Snapshot!.Layers);
        Assert.Equal(new ConfigurationLayers("base.yaml", "overlay.yaml", 2), full.Snapshot!.Layers);
        Assert.Equal(new ConfigurationLayers(null, null, 2), onlyVariables.Snapshot!.Layers);
    }

    [Fact]
    public void AnOverlayWithADocumentErrorStopsFieldValidation()
    {
        var error = Assert.Single(Validate("iskender: [\n", ("ISKENDER__PERSISTENCE__HOTS", "x")).Errors);

        Assert.Equal("config.document.malformed", error.Code.Id);
        Assert.Equal(ConfigurationLayer.Overlay, error.Location.Layer);
    }
}

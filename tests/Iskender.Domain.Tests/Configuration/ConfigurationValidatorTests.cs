using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class ConfigurationValidatorTests
{
    private const string Valid = "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"}}}}";

    private static ConfigurationDocumentSource Json(string name, string text) =>
        ConfigurationDocumentSource.FromString(name, ConfigurationDocumentFormat.Json, text);

    public static TheoryData<string> SidecarFixtures() =>
        [.. FixtureLoader.Fixtures("invalid").Concat(FixtureLoader.Fixtures("edge")).Where(path => FixtureLoader.Expected(path).Count > 0)];

    [Theory]
    [MemberData(nameof(SidecarFixtures))]
    public void EveryNegativeFixtureGivesItsSidecarThroughThePublicRoute(string fixture)
    {
        FixtureLoader.AssertMatchesSidecar(fixture);
    }

    [Fact]
    public void AValidDocumentGivesASnapshotWithItsLayers()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), Json("base.json", Valid), environmentName: "production"));

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
        Assert.Equal(0, report.TotalErrorCount);
        Assert.NotNull(report.Snapshot);
        Assert.Equal("production", report.Snapshot.EnvironmentName);
        Assert.Equal(new ConfigurationLayers("base.json", null, 0), report.Snapshot.Layers);
        Assert.Equal("db", report.Snapshot.GetSection(TestSchemas.Persistence).GetString("host"));
    }

    [Fact]
    public void ADocumentErrorStopsFieldValidationAndEveryDocumentIsStillRead()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), Json("base.json", "{\"iskender\":{\"nope\":1}}"), Json("overlay.json", "{\"a\":1,}")));

        var error = Assert.Single(report.Errors);
        Assert.Equal("config.document.malformed", error.Code.Id);
        Assert.Equal("(document)", error.Path);
        Assert.Equal(ConfigurationLayer.Overlay, error.Location.Layer);
        Assert.Null(report.Snapshot);

        var both = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), Json("base.json", "["), Json("overlay.json", "{\"a\":1,\"a\":2}")));

        Assert.Equal(["config.document.malformed", "config.document.duplicate_key"], both.Errors.Select(e => e.Code.Id));
        Assert.Equal([ConfigurationLayer.Base, ConfigurationLayer.Overlay], both.Errors.Select(e => e.Location.Layer));
    }

    [Fact]
    public void NoDocumentsValidatesTheDefaultsAlone()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry()));

        Assert.Equal(["iskender.persistence.host", "iskender.persistence.pool.name"], report.Errors.Select(error => error.Path));
        Assert.All(report.Errors, error => Assert.Equal("config.field.required", error.Code.Id));
        Assert.All(report.Errors, error => Assert.Equal(ConfigurationSourceLocation.None, error.Location));
    }

    [Fact]
    public void ARequestIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationValidator.Validate(null!));
    }
}

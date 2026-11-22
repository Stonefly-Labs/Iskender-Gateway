using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Roots;

/// <summary>Container contract, Configuration roots rules 1 to 7, and the Edge Cases on files.</summary>
public class ConfigurationRootsTests
{
    private const string Directory = "/config";
    private const string Valid = "{\"iskender\":{\"container\":{}}}";

    private static Dictionary<string, string?> Env(string? directory = Directory, string? environment = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (directory is not null)
        {
            values[ConfigurationRoots.DirectoryVariable] = directory;
        }

        if (environment is not null)
        {
            values[ConfigurationRoots.EnvironmentVariable] = environment;
        }

        return values;
    }

    private static GatewayException Rejected(Func<CompositionSources> resolve, string layer, string source)
    {
        var failure = Assert.Throws<GatewayException>(() => resolve());
        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Null(failure.InnerException);
        var values = failure.Error!.Diagnostics.Values;
        Assert.Contains(values, value => value.Key == "errorCount" && value.Value == "1");
        Assert.Contains(values, value => value.Key == "layer" && value.Value == layer);
        Assert.Contains(values, value => value.Key == "source" && value.Value == source);
        return failure;
    }

    [Fact]
    public void TheBaseYamlDocumentIsFoundAndNothingIsOpened()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.yaml", "iskender: {}");

        var sources = ConfigurationRoots.Resolve(Env(), files);

        Assert.Equal("iskender.yaml", sources.BaseDocument!.SourceName);
        Assert.Equal(ConfigurationDocumentFormat.Yaml, sources.BaseDocument.Format);
        Assert.Null(sources.OverlayDocument);
        Assert.True(sources.ReadEnvironmentVariables);
        Assert.Null(sources.EnvironmentName);
        Assert.Empty(files.Opened);
    }

    [Fact]
    public void TheBaseJsonDocumentIsFound()
    {
        var sources = ConfigurationRoots.Resolve(Env(), new TestFileSystem().WithFile("/config/iskender.json", Valid));

        Assert.Equal("iskender.json", sources.BaseDocument!.SourceName);
        Assert.Equal(ConfigurationDocumentFormat.Json, sources.BaseDocument.Format);
    }

    [Fact]
    public void TheOverlayIsFoundForTheEnvironment()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", Valid).WithFile("/config/iskender.staging.yaml", "iskender: {}");

        var sources = ConfigurationRoots.Resolve(Env(environment: "staging"), files);

        Assert.Equal("iskender.staging.yaml", sources.OverlayDocument!.SourceName);
        Assert.Equal("staging", sources.EnvironmentName);
    }

    [Fact]
    public void TheDefaultDirectoryIsUsedWhenTheVariableIsUnsetOrEmpty()
    {
        var files = new TestFileSystem().WithFile("/etc/iskender/iskender.yaml", "iskender: {}");

        Assert.Equal("iskender.yaml", ConfigurationRoots.Resolve(Env(directory: null), files).BaseDocument!.SourceName);
        Assert.Equal("iskender.yaml", ConfigurationRoots.Resolve(Env(directory: ""), files).BaseDocument!.SourceName);
    }

    [Fact]
    public void AnAbsentDefaultDirectoryIsNoDocuments()
    {
        var sources = ConfigurationRoots.Resolve(Env(directory: null, environment: "staging"), new TestFileSystem());

        Assert.Null(sources.BaseDocument);
        Assert.Null(sources.OverlayDocument);
        Assert.Equal("staging", sources.EnvironmentName);
    }

    [Fact]
    public void ANamedDirectoryThatDoesNotExistFails() =>
        Rejected(() => ConfigurationRoots.Resolve(Env(directory: "/missing"), new TestFileSystem()), "directory", ConfigurationRoots.DirectoryVariable);

    [Fact]
    public void ANamedDirectoryWithNoFilesIsNoDocuments()
    {
        var sources = ConfigurationRoots.Resolve(Env(), new TestFileSystem().WithDirectory(Directory));

        Assert.Null(sources.BaseDocument);
    }

    [Fact]
    public void BothFilesInTheBaseLayerFail()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.yaml", "iskender: {}").WithFile("/config/iskender.json", Valid);

        var failure = Rejected(() => ConfigurationRoots.Resolve(Env(), files), "base", "iskender.yaml and iskender.json");
        Assert.DoesNotContain("iskender: {}", failure.Error!.Diagnostics.Text ?? "", StringComparison.Ordinal);
        Assert.Empty(files.Opened);
    }

    [Fact]
    public void BothFilesInTheOverlayLayerFail()
    {
        var files = new TestFileSystem()
            .WithFile("/config/iskender.staging.yaml", "iskender: {}")
            .WithFile("/config/iskender.staging.json", Valid);

        Rejected(() => ConfigurationRoots.Resolve(Env(environment: "staging"), files), "overlay", "iskender.staging.yaml and iskender.staging.json");
    }

    [Fact]
    public void AnEnvironmentWithNoOverlayIsAMissingLayer()
    {
        var sources = ConfigurationRoots.Resolve(Env(environment: "staging"), new TestFileSystem().WithFile("/config/iskender.json", Valid));

        Assert.Null(sources.OverlayDocument);
        Assert.Equal("staging", sources.EnvironmentName);
    }

    [Fact]
    public void AnEmptyEnvironmentIsNone()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", Valid).WithFile("/config/iskender..json", Valid);

        var sources = ConfigurationRoots.Resolve(Env(environment: ""), files);

        Assert.Null(sources.EnvironmentName);
        Assert.Null(sources.OverlayDocument);
    }

    [Theory]
    [InlineData("Iskender.yaml")]
    [InlineData("iskender.YAML")]
    [InlineData("ISKENDER.json")]
    public void NamesMatchExactlyIncludingCase(string name)
    {
        var sources = ConfigurationRoots.Resolve(Env(), new TestFileSystem().WithFile($"/config/{name}", Valid));

        Assert.Null(sources.BaseDocument);
    }

    [Fact]
    public void AnOverlayNameInADifferentCaseIsNotTheOverlay()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", Valid).WithFile("/config/iskender.Staging.json", Valid);

        Assert.Null(ConfigurationRoots.Resolve(Env(environment: "staging"), files).OverlayDocument);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("Staging")]
    [InlineData("a/b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void AnInvalidEnvironmentNameFormsNoPathAndIsPassedOnAsGiven(string name)
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", Valid).WithFile($"/config/iskender.{name}.json", Valid);

        var sources = ConfigurationRoots.Resolve(Env(environment: name), files);

        Assert.Null(sources.OverlayDocument);
        Assert.Equal(name, sources.EnvironmentName);
    }

    [Fact]
    public void AnInvalidEnvironmentNameIsRejectedByTheConfigurationEngine()
    {
        var sources = ConfigurationRoots.Resolve(Env(environment: "../x"), new TestFileSystem().WithFile("/config/iskender.json", Valid));
        using var configuration = new CompositionConfiguration(sources, ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema]), null);

        var failure = Assert.Throws<GatewayException>(() => configuration.Validate(CancellationToken.None));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.environment.invalid", failure.Error!.Diagnostics.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentIsReadWhenValidated()
    {
        var files = new TestFileSystem().WithFile("/config/iskender.json", "{\"iskender\":{\"container\":{\"healthPort\":9001}}}");
        var sources = ConfigurationRoots.Resolve(Env(), files);
        using var configuration = new CompositionConfiguration(sources, ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema]), null);

        var snapshot = configuration.Validate(CancellationToken.None);

        Assert.Equal(9001, ContainerSettings.From(snapshot.GetSection(ContainerSectionSchema.Schema), snapshot.EnvironmentName, hasTrafficListener: false).HealthPort);
        Assert.Equal(["/config/iskender.json"], files.Opened);
    }

    [Fact]
    public void AnUnreadableFileFailsAtValidationNamingTheLayer()
    {
        var sources = ConfigurationRoots.Resolve(Env(), new TestFileSystem().WithUnreadableFile("/config/iskender.yaml"));
        using var configuration = new CompositionConfiguration(sources, ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema]), null);

        Rejected(() =>
        {
            configuration.Validate(CancellationToken.None);
            return sources;
        }, "base", "iskender.yaml");
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ConfigurationRoots.Resolve(null!, new TestFileSystem())).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ConfigurationRoots.Resolve(Env(), null!)).Code);
    }
}

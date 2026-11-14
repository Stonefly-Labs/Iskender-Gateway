using Iskender.Core.Contracts.Errors;

namespace Iskender.Hosting.Tests.Composition;

/// <summary>The construction rules of <see cref="IskenderConfigurationDocument"/> (data-model "Public surface", FR-011).</summary>
public class ConfigurationDocumentTests
{
    private static readonly string Longest = new('a', 256);

    public static TheoryData<string> BadNames() => ["", new string('a', 257), "base\nfile", "base\tfile", "base\u0000file", "base\u0085file", "base\u2028file", "base\u2029file"];

    [Fact]
    public void EachFormKeepsItsSourceNameAndFormat()
    {
        var file = IskenderConfigurationDocument.FromFile("/config/iskender.yaml", IskenderDocumentFormat.Yaml, "base");
        var text = IskenderConfigurationDocument.FromText("overlay", IskenderDocumentFormat.Json, "{}");
        var stream = IskenderConfigurationDocument.FromStream("stream", IskenderDocumentFormat.Yaml, () => Stream.Null);

        Assert.Equal(("base", IskenderDocumentFormat.Yaml), (file.SourceName, file.Format));
        Assert.Equal(("overlay", IskenderDocumentFormat.Json), (text.SourceName, text.Format));
        Assert.Equal(("stream", IskenderDocumentFormat.Yaml), (stream.SourceName, stream.Format));
    }

    [Fact]
    public void AFileWithNoSourceNameIsNamedAfterItsFileName()
    {
        Assert.Equal("iskender.yaml", IskenderConfigurationDocument.FromFile(Path.Combine("config", "iskender.yaml"), IskenderDocumentFormat.Yaml).SourceName);
    }

    [Fact]
    public void ASourceNameOfOneOr256CharactersIsAccepted()
    {
        Assert.Equal("a", IskenderConfigurationDocument.FromText("a", IskenderDocumentFormat.Json, "{}").SourceName);
        Assert.Equal(Longest, IskenderConfigurationDocument.FromText(Longest, IskenderDocumentFormat.Json, "{}").SourceName);
        Assert.Equal(Longest, IskenderConfigurationDocument.FromStream(Longest, IskenderDocumentFormat.Json, () => Stream.Null).SourceName);
        Assert.Equal(Longest, IskenderConfigurationDocument.FromFile("iskender.json", IskenderDocumentFormat.Json, Longest).SourceName);
    }

    [Theory]
    [MemberData(nameof(BadNames))]
    public void ASourceNameOutsideTheRuleFails(string name)
    {
        AssertInvalid(() => IskenderConfigurationDocument.FromText(name, IskenderDocumentFormat.Json, "{}"));
        AssertInvalid(() => IskenderConfigurationDocument.FromStream(name, IskenderDocumentFormat.Json, () => Stream.Null));
        AssertInvalid(() => IskenderConfigurationDocument.FromFile("iskender.json", IskenderDocumentFormat.Json, name));
    }

    [Fact]
    public void AFilePathWhoseFileNameIsEmptyFailsWithNoSourceName()
    {
        AssertInvalid(() => IskenderConfigurationDocument.FromFile("config/", IskenderDocumentFormat.Json));
    }

    [Fact]
    public void NullArgumentsFail()
    {
        AssertInvalid(() => IskenderConfigurationDocument.FromFile(null!, IskenderDocumentFormat.Json));
        AssertInvalid(() => IskenderConfigurationDocument.FromFile("", IskenderDocumentFormat.Json));
        AssertInvalid(() => IskenderConfigurationDocument.FromText(null!, IskenderDocumentFormat.Json, "{}"));
        AssertInvalid(() => IskenderConfigurationDocument.FromText("base", IskenderDocumentFormat.Json, null!));
        AssertInvalid(() => IskenderConfigurationDocument.FromStream(null!, IskenderDocumentFormat.Json, () => Stream.Null));
        AssertInvalid(() => IskenderConfigurationDocument.FromStream("base", IskenderDocumentFormat.Json, null!));
    }

    [Fact]
    public void AnUndefinedFormatFails()
    {
        var undefined = (IskenderDocumentFormat)2;

        AssertInvalid(() => IskenderConfigurationDocument.FromFile("iskender.json", undefined));
        AssertInvalid(() => IskenderConfigurationDocument.FromText("base", undefined, "{}"));
        AssertInvalid(() => IskenderConfigurationDocument.FromStream("base", undefined, () => Stream.Null));
    }

    [Fact]
    public void NothingIsOpenedOrCalledAtConstruction()
    {
        var calls = 0;
        var missing = Path.Combine(Path.GetTempPath(), $"iskender-{Guid.NewGuid():N}", "absent.yaml");

        IskenderConfigurationDocument.FromFile(missing, IskenderDocumentFormat.Yaml);
        IskenderConfigurationDocument.FromStream("base", IskenderDocumentFormat.Yaml, () =>
        {
            calls++;
            return Stream.Null;
        });
        IskenderConfigurationDocument.FromText("base", IskenderDocumentFormat.Yaml, "not: [valid");

        Assert.Equal(0, calls);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void OptionsStartWithNoDocumentsNoEnvironmentVariablesAndNoEnvironmentName()
    {
        var options = new IskenderOptions();

        Assert.Null(options.BaseDocument);
        Assert.Null(options.OverlayDocument);
        Assert.False(options.ReadEnvironmentVariables);
        Assert.Null(options.EnvironmentName);
    }

    private static void AssertInvalid(Func<IskenderConfigurationDocument> create)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(create).Code);
    }
}

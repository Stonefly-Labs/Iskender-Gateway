using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public sealed class SourceTests : IDisposable
{
    private const int Limit = 1_048_576;
    private readonly string _directory = Directory.CreateTempSubdirectory("iskender-config-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void AStreamIsReadNoFurtherThanTheLimitPlusOneByte()
    {
        var stream = new CountingStream();

        var source = ConfigurationDocumentSource.FromStream("big.json", ConfigurationDocumentFormat.Json, stream);

        Assert.Equal(Limit + 1, stream.BytesRead);
        Assert.Equal(Limit + 1, source.Content.Length);
    }

    [Fact]
    public void ASmallStreamIsReadWhole()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"iskender\":{}}"));

        var source = ConfigurationDocumentSource.FromStream("small.json", ConfigurationDocumentFormat.Json, stream);

        Assert.Equal("{\"iskender\":{}}", Encoding.UTF8.GetString(source.Content));
        Assert.Equal("small.json", source.Name);
        Assert.Equal(ConfigurationDocumentFormat.Json, source.Format);
    }

    [Fact]
    public void AFileIsReadNoFurtherThanTheLimitPlusOneByteAndNamedByItsFileName()
    {
        var path = Path.Combine(_directory, "gateway.yaml");
        File.WriteAllBytes(path, new byte[Limit + 4096]);

        var source = ConfigurationDocumentSource.FromFile(path, ConfigurationDocumentFormat.Yaml);

        Assert.Equal(Limit + 1, source.Content.Length);
        Assert.Equal("gateway.yaml", source.Name);
        Assert.Equal("custom", ConfigurationDocumentSource.FromFile(path, ConfigurationDocumentFormat.Yaml, "custom").Name);
    }

    [Fact]
    public void BytesAreKeptToTheLimitPlusOneByte()
    {
        Assert.Equal(Limit + 1, ConfigurationDocumentSource.FromBytes("a", ConfigurationDocumentFormat.Json, new byte[Limit + 10]).Content.Length);
        Assert.Equal(Limit, ConfigurationDocumentSource.FromBytes("a", ConfigurationDocumentFormat.Json, new byte[Limit]).Content.Length);
    }

    [Fact]
    public void TextIsUtf8WithNoByteOrderMark()
    {
        Assert.Equal([0xC3, 0xA9], ConfigurationDocumentSource.FromString("a", ConfigurationDocumentFormat.Yaml, "é").Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    public void AnEmptyOrControlCharacterNameIsRejected(string name)
    {
        Assert.Equal(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ConfigurationDocumentSource.FromString(name, ConfigurationDocumentFormat.Json, "{}")).Code);
    }

    [Fact]
    public void AnUndefinedFormatOrAnOverlongNameIsRejected()
    {
        Assert.Throws<GatewayException>(() => ConfigurationDocumentSource.FromString("a", (ConfigurationDocumentFormat)7, "{}"));
        Assert.Throws<GatewayException>(() => ConfigurationDocumentSource.FromString(new string('a', 257), ConfigurationDocumentFormat.Json, "{}"));
    }

    [Fact]
    public void EnvironmentSourceKeepsTheGivenVariables()
    {
        var source = ConfigurationEnvironmentSource.FromDictionary([KeyValuePair.Create("ISKENDER__A__B", "1"), KeyValuePair.Create("PATH", "/bin")]);

        Assert.Equal("1", source.Variables["ISKENDER__A__B"]);
        Assert.Equal(2, source.Variables.Count);
    }

    [Fact]
    public void EnvironmentSourceFromTheProcessTakesOnlyPrefixedNames()
    {
        var source = ConfigurationEnvironmentSource.FromProcess();

        Assert.All(source.Variables.Keys, name => Assert.StartsWith("ISKENDER__", name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ALoadRequestNeedsARegistry()
    {
        Assert.Throws<GatewayException>(() => new ConfigurationLoadRequest(null!));
        var request = new ConfigurationLoadRequest(TestSchemas.Registry(), environmentName: "production");

        Assert.Null(request.BaseDocument);
        Assert.Null(request.OverlayDocument);
        Assert.Null(request.Environment);
        Assert.Equal("production", request.EnvironmentName);
    }
}

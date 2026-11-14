using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Hosting.Configuration;

namespace Iskender.Hosting.Tests.Configuration;

/// <summary>The <c>hosting</c> section, validated alone through ISK-0003 (hosting contract, Configuration; research R13).</summary>
public class HostingSectionSchemaTests
{
    private static ConfigurationValidationReport Validate(string document) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([HostingSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("hosting.json", ConfigurationDocumentFormat.Json, document),
            environmentName: "production"));

    private static TimeSpan Read(string document)
    {
        var report = Validate(document);
        Assert.True(report.IsValid, report.ToString());
        return HostingSettings.From(report.Snapshot!).RequestTimeout;
    }

    private static string OnlyError(string document)
    {
        var report = Validate(document);
        Assert.False(report.IsValid);
        return Assert.Single(report.Errors).Code.Id;
    }

    [Fact]
    public void TheSectionIsNamespaceHostingVersionOneOwnedByHosting()
    {
        Assert.Equal("hosting", HostingSectionSchema.Schema.Namespace);
        Assert.Equal(1, HostingSectionSchema.Schema.Version);
        Assert.Equal("Iskender.Hosting", HostingSectionSchema.Schema.OwnerModule);
        var field = Assert.Single(HostingSectionSchema.Schema.Fields);
        Assert.Equal("requestTimeout", field.Name);
        Assert.False(field.IsDevelopmentOnly);
    }

    [Theory]
    [InlineData("{\"iskender\":{}}")]
    [InlineData("{\"iskender\":{\"hosting\":{}}}")]
    public void WithNoValueTheTimeoutIs100Seconds(string document)
    {
        Assert.Equal(TimeSpan.FromSeconds(100), Read(document));
    }

    [Theory]
    [InlineData("1s", 1)]
    [InlineData("1h", 3600)]
    [InlineData("30s", 30)]
    public void ATimeoutWithinTheRangeIsAccepted(string value, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Read("{\"iskender\":{\"hosting\":{\"requestTimeout\":\"" + value + "\"}}}"));
    }

    [Theory]
    [InlineData("999ms")]
    [InlineData("3601s")]
    public void ATimeoutOutsideTheRangeIsRejected(string value)
    {
        Assert.Equal(ErrorCodes.ConfigFieldOutOfRange.Id, OnlyError("{\"iskender\":{\"hosting\":{\"requestTimeout\":\"" + value + "\"}}}"));
    }

    [Fact]
    public void AnUnknownFieldIsRejected()
    {
        Assert.Equal("config.field.unknown", OnlyError("{\"iskender\":{\"hosting\":{\"requestTimeoutMs\":100}}}"));
    }

    [Fact]
    public void ANullSnapshotFails()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => HostingSettings.From(null!)).Code);
    }
}

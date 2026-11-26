using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Settings;

/// <summary>The <c>container</c> section and the settings read from it (container contract, The <c>container</c> section).</summary>
public class ContainerSettingsTests
{
    private static ConfigurationValidationReport Validate(string fields, string? environment = null) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("container.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"container\":{" + fields + "}}}"),
            environmentName: environment));

    private static ContainerSettings Read(string fields = "")
    {
        var report = Validate(fields);
        Assert.True(report.IsValid, report.ToString());
        return ContainerSettings.From(report.Snapshot!.GetSection(ContainerSectionSchema.Schema), environmentName: null, hasTrafficListener: false);
    }

    private static string OnlyError(string fields)
    {
        var report = Validate(fields);
        Assert.False(report.IsValid);
        return Assert.Single(report.Errors).Code.Id;
    }

    [Fact]
    public void TheSchemaHasExactlyTheContractsFieldsAndNoneIsDevelopmentOnly()
    {
        Assert.Equal("container", ContainerSectionSchema.Schema.Namespace);
        Assert.Equal("Iskender.Infrastructure.Container", ContainerSectionSchema.Schema.OwnerModule);
        Assert.Equal(2, ContainerSectionSchema.Schema.Version);
        Assert.Equal(
            ["trafficPort", "healthPort", "shutdownDelay", "shutdownTimeout", "readinessCacheDuration", "trafficTransport", "tlsCertificateFile", "tlsKeyFile"],
            ContainerSectionSchema.Schema.Fields.Select(field => field.Name));
        Assert.All(ContainerSectionSchema.Schema.Fields, field => Assert.False(field.IsDevelopmentOnly));
    }

    [Fact]
    public void DefaultsAreTheContracts()
    {
        var settings = Read();

        Assert.Equal(8080, settings.TrafficPort);
        Assert.Equal(8081, settings.HealthPort);
        Assert.Equal(TimeSpan.Zero, settings.ShutdownDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.ShutdownTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.ReadinessCacheDuration);
    }

    [Fact]
    public void AnAbsentSectionGivesTheDefaults()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(ConfigurationSchemaRegistry.Create([ContainerSectionSchema.Schema])));
        Assert.True(report.IsValid, report.ToString());

        Assert.Equal(8081, ContainerSettings.From(report.Snapshot!.GetSection(ContainerSectionSchema.Schema), environmentName: null, hasTrafficListener: false).HealthPort);
    }

    [Fact]
    public void EachFieldIsRead()
    {
        var settings = Read("\"trafficPort\":9000,\"healthPort\":9001,\"shutdownDelay\":\"5s\",\"shutdownTimeout\":\"2m\",\"readinessCacheDuration\":\"0s\"");

        Assert.Equal(9000, settings.TrafficPort);
        Assert.Equal(9001, settings.HealthPort);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.ShutdownDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), settings.ShutdownTimeout);
        Assert.Equal(TimeSpan.Zero, settings.ReadinessCacheDuration);
    }

    [Theory]
    [InlineData("\"trafficPort\":1024,\"healthPort\":65535")]
    [InlineData("\"shutdownDelay\":\"1m\"")]
    [InlineData("\"shutdownTimeout\":\"1s\"")]
    [InlineData("\"shutdownTimeout\":\"5m\"")]
    [InlineData("\"readinessCacheDuration\":\"30s\"")]
    public void TheBoundsAreInclusive(string fields) => Read(fields);

    [Theory]
    [InlineData("\"trafficPort\":1023")]
    [InlineData("\"trafficPort\":65536")]
    [InlineData("\"healthPort\":1023")]
    [InlineData("\"healthPort\":65536")]
    [InlineData("\"shutdownDelay\":\"61s\"")]
    [InlineData("\"shutdownTimeout\":\"0s\"")]
    [InlineData("\"shutdownTimeout\":\"301s\"")]
    [InlineData("\"readinessCacheDuration\":\"31s\"")]
    public void AValueOutsideItsRangeIsRejected(string fields) => Assert.Equal("config.field.out_of_range", OnlyError(fields));

    [Theory]
    [InlineData("\"trafficPort\":\"8080\"")]
    [InlineData("\"healthPort\":true")]
    [InlineData("\"shutdownDelay\":5")]
    [InlineData("\"shutdownTimeout\":{}")]
    [InlineData("\"readinessCacheDuration\":[]")]
    public void AValueOfTheWrongTypeIsRejected(string fields) => Assert.Equal("config.field.type_mismatch", OnlyError(fields));

    [Fact]
    public void AnUnknownFieldIsRejected() => Assert.Equal("config.field.unknown", OnlyError("\"port\":8080"));

    [Fact]
    public void EqualPortsFailNamingHealthPort()
    {
        var report = Validate("\"trafficPort\":9000,\"healthPort\":9000");
        Assert.True(report.IsValid);

        var failure = Assert.Throws<GatewayException>(() => ContainerSettings.From(report.Snapshot!.GetSection(ContainerSectionSchema.Schema), environmentName: null, hasTrafficListener: false));

        Assert.Same(ErrorCodes.ConfigFieldOutOfRange, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "field" && value.Value == "iskender.container.healthPort");
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void ANullSectionIsRejected() =>
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ContainerSettings.From(null!, environmentName: null, hasTrafficListener: false)).Code);
}

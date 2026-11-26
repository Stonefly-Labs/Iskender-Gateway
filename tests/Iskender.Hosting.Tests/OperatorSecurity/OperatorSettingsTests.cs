using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Management.Security;

namespace Iskender.Hosting.Tests.OperatorSecurity;

/// <summary>
/// The <c>iskender.operator</c> section (ISK-0010 T012, operator-security contract Configuration, research R16, FR-033):
/// exactly three fields, every default, the edges of each bound, and that a refusal names the field and holds no value.
/// </summary>
public class OperatorSettingsTests
{
    [Fact]
    public void AnEmptySectionReadsEveryDefault()
    {
        var settings = Read("");

        Assert.Equal(TimeSpan.FromHours(1), settings.TokenLifetime);
        Assert.Equal(10, settings.AttemptsPerMinute);
        Assert.Equal(TimeSpan.FromHours(24), settings.EnrollmentCodeLifetime);
    }

    [Fact]
    public void TheSectionHasExactlyTheThreeContractFields()
    {
        var leaves = new List<string>();
        foreach (var group in OperatorSectionSchema.Schema.Fields)
        {
            Assert.Equal(ConfigurationFieldKind.Section, group.Kind);
            leaves.AddRange(group.Fields.Select(field => $"{group.Name}.{field.Name}"));
        }

        Assert.Equal(["bootstrap.attemptsPerMinute", "bootstrap.tokenLifetime", "enrollment.codeLifetime"], leaves.Order(StringComparer.Ordinal));
        Assert.Equal(("operator", 1), (OperatorSectionSchema.Schema.Namespace, OperatorSectionSchema.Schema.Version));
    }

    // Each field's lowest and highest valid value, and the nearest invalid value on each side.
    public static TheoryData<string, string, string, string, string> Bounds() => new()
    {
        { "bootstrap.tokenLifetime", "299s", "5m", "24h", "86401s" },
        { "bootstrap.attemptsPerMinute", "0", "1", "600", "601" },
        { "enrollment.codeLifetime", "3599s", "1h", "7d", "604801s" },
    };

    [Theory]
    [MemberData(nameof(Bounds))]
    public void EachBoundAcceptsItsEdgesAndRefusesBeyondThemNamingTheField(string field, string below, string lowest, string highest, string above)
    {
        Assert.True(Validate(Field(field, lowest)).IsValid);
        Assert.True(Validate(Field(field, highest)).IsValid);
        foreach (var refused in new[] { below, above })
        {
            var report = Validate(Field(field, refused));
            Assert.False(report.IsValid);
            var error = Assert.Single(report.Errors);
            Assert.Equal((ErrorCodes.ConfigFieldOutOfRange.Id, $"iskender.operator.{field}"), (error.Code.Id, error.Path));
        }
    }

    [Fact]
    public void TheEdgesReadAsTheirValues()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Read(Field("bootstrap.tokenLifetime", "5m")).TokenLifetime);
        Assert.Equal(600, Read(Field("bootstrap.attemptsPerMinute", "600")).AttemptsPerMinute);
        Assert.Equal(TimeSpan.FromDays(7), Read(Field("enrollment.codeLifetime", "7d")).EnrollmentCodeLifetime);
    }

    [Fact]
    public void NoFieldIsDevelopmentOnly()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([OperatorSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("operator.json", ConfigurationDocumentFormat.Json, Document(Field("bootstrap.attemptsPerMinute", "600"))),
            environmentName: null));

        Assert.True(report.IsValid, report.ToString());
    }

    private static string Field(string field, string value)
    {
        var (group, name) = (field.Split('.')[0], field.Split('.')[1]);
        var json = long.TryParse(value, out _) ? value : $"\"{value}\"";
        return $"\"{group}\":{{\"{name}\":{json}}}";
    }

    private static string Document(string fields) => "{\"iskender\":{\"operator\":{" + fields + "}}}";

    private static ConfigurationValidationReport Validate(string fields) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([OperatorSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("operator.json", ConfigurationDocumentFormat.Json, Document(fields)),
            environmentName: "development"));

    private static OperatorSettings Read(string fields)
    {
        var report = Validate(fields);
        Assert.True(report.IsValid, report.ToString());
        return OperatorSettings.From(report.Snapshot!);
    }
}

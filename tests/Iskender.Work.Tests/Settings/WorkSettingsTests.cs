using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Settings;

/// <summary>
/// The <c>iskender.work</c> section (ISK-0009 T015, <c>work</c> contract Configuration, research R13, FR-029): every
/// default, the edges of each bound, each check made when the settings are read, and that no failure holds a value.
/// </summary>
public class WorkSettingsTests
{
    [Fact]
    public void AnEmptySectionReadsEveryDefault()
    {
        var settings = Read("");

        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollInterval);
        Assert.Equal(32, settings.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.LeaseDuration);
        Assert.Equal(4, settings.Shards);
        Assert.Equal(16, settings.Concurrency);
        Assert.Equal(4, settings.ScopeConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.HandlerTimeout);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.InitialDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.MaximumDelay);
        Assert.Equal(8, settings.MaximumAttempts);
        Assert.Equal(TimeSpan.FromDays(7), settings.CompletedRetention);
        Assert.Equal(TimeSpan.FromDays(30), settings.PoisonedRetention);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.PurgeExpiredInterval);
        Assert.Equal(500, settings.PurgeExpiredBatch);
        Assert.Equal(TimeSpan.FromHours(1), settings.RetentionInterval);
        Assert.Equal(500, settings.RetentionBatch);
    }

    [Fact]
    public void TheSectionHasExactlyTheSixteenContractFieldsAndNoPayloadLimit()
    {
        var leaves = new List<string>();
        void Walk(IEnumerable<FieldSchema> fields, string prefix)
        {
            foreach (var field in fields)
            {
                if (field.Kind == ConfigurationFieldKind.Section)
                {
                    Walk(field.Fields, prefix + field.Name + ".");
                }
                else
                {
                    leaves.Add(prefix + field.Name);
                }
            }
        }

        Walk(WorkSectionSchema.Schema.Fields, "");

        Assert.Equal(
            [
                "batchSize", "dispatch.concurrency", "dispatch.scopeConcurrency", "dispatch.shards", "handler.timeout",
                "jobs.purgeExpired.batch", "jobs.purgeExpired.interval", "jobs.retention.batch", "jobs.retention.interval",
                "leaseDuration", "pollInterval", "retention.completed", "retention.poisoned", "retry.initialDelay",
                "retry.maximumAttempts", "retry.maximumDelay",
            ],
            leaves.Order(StringComparer.Ordinal));
        Assert.Equal(("work", 1), (WorkSectionSchema.Schema.Namespace, WorkSectionSchema.Schema.Version));
    }

    // Each field's lowest and highest valid value, and the nearest invalid value on each side.
    public static TheoryData<string, string, string, string, string> Bounds() => new()
    {
        { "pollInterval", "999ms", "1s", "5m", "301s" },
        { "batchSize", "0", "1", "1000", "1001" },
        { "dispatch.concurrency", "0", "1", "256", "257" },
        { "handler.timeout", "999ms", "1s", "15m", "901s" },
        { "retry.initialDelay", "99ms", "100ms", "1h", "3601s" },
        { "retry.maximumDelay", "999ms", "1s", "24h", "86401s" },
        { "retry.maximumAttempts", "0", "1", "100", "101" },
        { "retention.completed", "3599s", "1h", "365d", "366d" },
        { "retention.poisoned", "23h", "1d", "365d", "366d" },
        { "jobs.purgeExpired.interval", "59s", "1m", "24h", "1441m" },
        { "jobs.purgeExpired.batch", "0", "1", "10000", "10001" },
        { "jobs.retention.interval", "59s", "1m", "24h", "1441m" },
        { "jobs.retention.batch", "0", "1", "10000", "10001" },
    };

    [Theory]
    [MemberData(nameof(Bounds))]
    public void EachBoundAcceptsItsEdgesAndRefusesBeyondThem(string field, string below, string lowest, string highest, string above)
    {
        Assert.False(Validate(Field(field, below)).IsValid);
        Assert.True(Validate(Field(field, lowest)).IsValid);
        Assert.True(Validate(Field(field, highest)).IsValid);
        Assert.False(Validate(Field(field, above)).IsValid);
        Assert.Contains(Validate(Field(field, above)).Errors, error => error.Path == $"iskender.work.{field}");
    }

    [Fact]
    public void LeaseShardsAndScopeConcurrencyHaveTheirSchemaBounds()
    {
        // These three also take part in read-time checks, so their edges are read with the checks satisfied.
        Assert.False(Validate(Field("leaseDuration", "4s")).IsValid);
        Assert.Equal(TimeSpan.FromSeconds(5), Read(Field("leaseDuration", "5s")).LeaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), Read(Field("leaseDuration", "10m")).LeaseDuration);
        Assert.False(Validate(Field("leaseDuration", "601s")).IsValid);
        Assert.False(Validate(Field("dispatch.shards", "0")).IsValid);
        Assert.Equal(1, Read(Field("dispatch.shards", "1")).Shards);
        Assert.Equal(64, Read(Field("dispatch.shards", "64")).Shards);
        Assert.False(Validate(Field("dispatch.shards", "65")).IsValid);
        Assert.False(Validate(Field("dispatch.scopeConcurrency", "0")).IsValid);
        Assert.Equal(1, Read(Field("dispatch.scopeConcurrency", "1")).ScopeConcurrency);
        Assert.Equal(256, Read("\"dispatch\":{\"concurrency\":256,\"scopeConcurrency\":256}").ScopeConcurrency);
        Assert.False(Validate("\"dispatch\":{\"concurrency\":256,\"scopeConcurrency\":257}").IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void AShardCountThatDividesSixtyFourIsAccepted(int shards)
    {
        Assert.Equal(shards, Read(Field("dispatch.shards", shards.ToString(System.Globalization.CultureInfo.InvariantCulture))).Shards);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(48)]
    [InlineData(63)]
    public void AShardCountThatDoesNotDivideSixtyFourIsOutOfRange(int shards)
    {
        AssertOutOfRange(Field("dispatch.shards", shards.ToString(System.Globalization.CultureInfo.InvariantCulture)), "iskender.work.dispatch.shards");
    }

    [Fact]
    public void ScopeConcurrencyAboveConcurrencyIsOutOfRange()
    {
        Assert.Equal(8, Read("\"dispatch\":{\"concurrency\":8,\"scopeConcurrency\":8}").ScopeConcurrency);
        AssertOutOfRange("\"dispatch\":{\"concurrency\":8,\"scopeConcurrency\":9}", "iskender.work.dispatch.scopeConcurrency");
    }

    [Fact]
    public void AMaximumDelayBelowTheInitialDelayIsOutOfRange()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), Read("\"retry\":{\"initialDelay\":\"10s\",\"maximumDelay\":\"10s\"}").MaximumDelay);
        AssertOutOfRange("\"retry\":{\"initialDelay\":\"10s\",\"maximumDelay\":\"9s\"}", "iskender.work.retry.maximumDelay");
    }

    [Fact]
    public void ALeaseDurationOutsideTheCoordinationLeaseBoundsIsOutOfRange()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), Read(Field("leaseDuration", "20s"), "\"leases\":{\"minimumDuration\":\"20s\",\"maximumDuration\":\"40s\"}").LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(40), Read(Field("leaseDuration", "40s"), "\"leases\":{\"minimumDuration\":\"20s\",\"maximumDuration\":\"40s\"}").LeaseDuration);
        AssertOutOfRange(Field("leaseDuration", "19s"), "iskender.work.leaseDuration", "\"leases\":{\"minimumDuration\":\"20s\",\"maximumDuration\":\"40s\"}");
        AssertOutOfRange(Field("leaseDuration", "41s"), "iskender.work.leaseDuration", "\"leases\":{\"minimumDuration\":\"20s\",\"maximumDuration\":\"40s\"}");
    }

    private static void AssertOutOfRange(string fields, string path, string coordination = "")
    {
        var failure = Assert.Throws<GatewayException>(() => Read(fields, coordination));

        Assert.Same(ErrorCodes.ConfigFieldOutOfRange, failure.Code);
        // The error's only diagnostic is the field's path, so it holds no configured value.
        var value = Assert.Single(failure.Error!.Diagnostics.Values);
        Assert.Equal(("field", path), (value.Key, value.Value));
    }

    // A dotted field as nested JSON, with the value written as JSON: a number as is, anything else quoted.
    private static string Field(string field, string value)
    {
        var parts = field.Split('.');
        var json = long.TryParse(value, out _) ? value : $"\"{value}\"";
        for (var index = parts.Length - 1; index >= 0; index--)
        {
            json = index == parts.Length - 1 ? $"\"{parts[index]}\":{json}" : $"\"{parts[index]}\":{{{json}}}";
        }

        return json;
    }

    private static ConfigurationValidationReport Validate(string fields, string coordination = "") =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([WorkSectionSchema.Schema, CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString(
                "work.json",
                ConfigurationDocumentFormat.Json,
                "{\"iskender\":{\"work\":{" + fields + "},\"coordination\":{" + coordination + "}}}"),
            environmentName: "development"));

    private static WorkSettings Read(string fields, string coordination = "")
    {
        var report = Validate(fields, coordination);
        Assert.True(report.IsValid, report.ToString());
        return WorkSettings.From(report.Snapshot!, CoordinationSettings.From(report.Snapshot!));
    }
}

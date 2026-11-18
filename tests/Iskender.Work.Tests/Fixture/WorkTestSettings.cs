using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Fixture;

/// <summary>Validated <c>work</c> and <c>coordination</c> settings from JSON field text, for the dispatcher harness.</summary>
internal static class WorkTestSettings
{
    /// <summary>
    /// The shortest shard lease, 5 s, a 1 s poll, and short retries, so a taken-over item is due again before an abandoned
    /// or paused lease expires.
    /// </summary>
    public const string Fast = "\"pollInterval\":\"1s\",\"leaseDuration\":\"5s\",\"retry\":{\"initialDelay\":\"100ms\",\"maximumDelay\":\"1s\"}";

    /// <summary>Reads <paramref name="work"/> and <paramref name="coordination"/>, each the fields of its section as JSON text without braces.</summary>
    public static (WorkSettings Work, CoordinationSettings Coordination) Read(string work = "", string coordination = "")
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([WorkSectionSchema.Schema, CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString(
                "work.json",
                ConfigurationDocumentFormat.Json,
                "{\"iskender\":{\"work\":{" + work + "},\"coordination\":{" + coordination + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        var coordinationSettings = CoordinationSettings.From(report.Snapshot!);
        return (WorkSettings.From(report.Snapshot!, coordinationSettings), coordinationSettings);
    }
}

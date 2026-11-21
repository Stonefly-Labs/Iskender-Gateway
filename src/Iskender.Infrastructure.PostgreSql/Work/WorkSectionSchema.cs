using Iskender.Core.Contracts.Configuration;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The ISK-0003 section <c>iskender.work</c>, version 1 (<c>work</c> contract Configuration, research R13): exactly the
/// sixteen fields, defaults, and bounds of the contract. Every default is a safety bound, so a worker with no section is
/// valid. There is no payload-limit field (R14). Owned by ISK-0009.
/// </summary>
internal static class WorkSectionSchema
{
    public const string Namespace = "work";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Infrastructure.PostgreSql", 1).WithFields(
        Duration("pollInterval", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5)).WithDefault(TimeSpan.FromSeconds(5)),
        Integer("batchSize", 1, 1_000).WithDefault(32L),
        Duration("leaseDuration", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10)).WithDefault(TimeSpan.FromSeconds(30)),
        FieldSchema.Create("dispatch", ConfigurationFieldKind.Section).WithFields(
            Integer("shards", 1, 64).WithDefault(4L),
            Integer("concurrency", 1, 256).WithDefault(16L),
            Integer("scopeConcurrency", 1, 256).WithDefault(4L)),
        FieldSchema.Create("handler", ConfigurationFieldKind.Section).WithFields(
            Duration("timeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15)).WithDefault(TimeSpan.FromSeconds(30))),
        FieldSchema.Create("retry", ConfigurationFieldKind.Section).WithFields(
            Duration("initialDelay", TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1)).WithDefault(TimeSpan.FromSeconds(1)),
            Duration("maximumDelay", TimeSpan.FromSeconds(1), TimeSpan.FromHours(24)).WithDefault(TimeSpan.FromMinutes(5)),
            Integer("maximumAttempts", 1, 100).WithDefault(8L)),
        FieldSchema.Create("retention", ConfigurationFieldKind.Section).WithFields(
            Duration("completed", TimeSpan.FromHours(1), TimeSpan.FromDays(365)).WithDefault(TimeSpan.FromDays(7)),
            Duration("poisoned", TimeSpan.FromDays(1), TimeSpan.FromDays(365)).WithDefault(TimeSpan.FromDays(30))),
        FieldSchema.Create("jobs", ConfigurationFieldKind.Section).WithFields(
            FieldSchema.Create("purgeExpired", ConfigurationFieldKind.Section).WithFields(
                Duration("interval", TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)).WithDefault(TimeSpan.FromMinutes(5)),
                Integer("batch", 1, 10_000).WithDefault(500L)),
            FieldSchema.Create("retention", ConfigurationFieldKind.Section).WithFields(
                Duration("interval", TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)).WithDefault(TimeSpan.FromHours(1)),
                Integer("batch", 1, 10_000).WithDefault(500L))));

    private static FieldSchema Integer(string name, long minimum, long maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(minimum, maximum));

    private static FieldSchema Duration(string name, TimeSpan minimum, TimeSpan maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(minimum, maximum));
}

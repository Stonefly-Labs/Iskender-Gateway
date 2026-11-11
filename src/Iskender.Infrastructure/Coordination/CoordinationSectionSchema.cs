using Iskender.Core.Contracts.Configuration;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The ISK-0003 section <c>iskender.coordination</c> (FR-027, research R16): exactly the data model's Configuration
/// table. Every field has a default, so a configuration with no section at all is valid and coordinates locally. There
/// is no connection string field, so no password can be embedded in one, and the Redis password is a secret reference.
/// Owned by ISK-0005.
/// </summary>
public static class CoordinationSectionSchema
{
    public const string Namespace = "coordination";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Infrastructure", 1).WithFields(
        Enumeration("topology", "single_instance", "multi_replica").WithDefault("multi_replica"),
        FieldSchema.Create("atomic", ConfigurationFieldKind.Section).WithFields(Coordinator()),
        FieldSchema.Create("leases", ConfigurationFieldKind.Section).WithFields(
            Coordinator(),
            Duration("minimumDuration", TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(1)).WithDefault(TimeSpan.FromSeconds(1)),
            Duration("maximumDuration", TimeSpan.FromSeconds(1), TimeSpan.FromHours(24)).WithDefault(TimeSpan.FromMinutes(10)),
            Duration("safetyMargin", TimeSpan.Zero, TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.FromSeconds(2)),
            Duration("maximumWait", TimeSpan.Zero, TimeSpan.FromMinutes(5)).WithDefault(TimeSpan.FromSeconds(30))),
        FieldSchema.Create("cache", ConfigurationFieldKind.Section).WithFields(
            Coordinator(),
            Integer("maximumValueBytes", 1, 1_048_576).WithDefault(65_536L),
            Integer("maximumEntries", 1, 1_000_000).WithDefault(10_000L)),
        FieldSchema.Create("keys", ConfigurationFieldKind.Section).WithFields(
            Duration("maximumExpiry", TimeSpan.FromSeconds(1), TimeSpan.FromDays(400)).WithDefault(TimeSpan.FromDays(35))),
        Duration("operationTimeout", TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.FromSeconds(5)),
        Duration("healthProbeTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.FromSeconds(5)),
        FieldSchema.Create("instanceLease", ConfigurationFieldKind.Section).WithFields(
            Duration("duration", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)).WithDefault(TimeSpan.FromSeconds(30))),
        FieldSchema.Create("redis", ConfigurationFieldKind.Section).WithFields(
            FieldSchema.Create("endpoints", ConfigurationFieldKind.Array)
                .WithItems(FieldSchema.Create("endpoint", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None
                    .WithLength(3, 261)
                    .WithAllowedCharacters(new CharacterClassPattern(
                        CharacterClasses.LowerCaseLetters | CharacterClasses.UpperCaseLetters | CharacterClasses.Digits, ".-_:[]"))))
                .WithConstraints(FieldConstraints.None.WithItemCount(0, 16)),
            Integer("database", 0, 15).WithDefault(0L),
            FieldSchema.Create("username", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithLength(1, 256)),
            FieldSchema.Create("password", ConfigurationFieldKind.SecretReference),
            Enumeration("tlsMode", "require", "disable").WithDefault("require").DevelopmentOnly(),
            Duration("connectTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.FromSeconds(5))));

    private static FieldSchema Coordinator() => Enumeration("coordinator", "local", "redis", "postgresql").WithDefault("local");

    private static FieldSchema Enumeration(string name, params string[] members) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Enumeration).WithConstraints(FieldConstraints.None.WithMembers(members));

    private static FieldSchema Integer(string name, long minimum, long maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(minimum, maximum));

    private static FieldSchema Duration(string name, TimeSpan minimum, TimeSpan maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(minimum, maximum));
}

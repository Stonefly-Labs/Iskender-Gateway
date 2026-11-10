using Iskender.Core.Contracts.Configuration;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// The ISK-0003 section <c>iskender.persistence</c> (FR-029, research R16): exactly the contract's Configuration table.
/// There is no connection string field, so no password can be embedded in one, and both passwords are secret
/// references. Owned by ISK-0004.
/// </summary>
public static class PostgreSqlSectionSchema
{
    public const string Namespace = "persistence";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Infrastructure.PostgreSql", 1).WithFields(
        Text("host").Required(),
        Integer("port", 1, 65535).WithDefault(5432L),
        Text("database").Required(),
        Text("username").Required(),
        FieldSchema.Create("password", ConfigurationFieldKind.SecretReference).Required(),
        FieldSchema.Create("tlsMode", ConfigurationFieldKind.Enumeration)
            .WithConstraints(FieldConstraints.None.WithMembers("require", "disable"))
            .WithDefault("require")
            .DevelopmentOnly(),
        Integer("poolMinimum", 0, 1000).WithDefault(0L),
        Integer("poolMaximum", 1, 1000).WithDefault(20L),
        Duration("connectionTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5)).WithDefault(TimeSpan.FromSeconds(15)),
        Duration("statementTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)).WithDefault(TimeSpan.FromSeconds(30)),
        Duration("healthProbeTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.FromSeconds(5)),
        FieldSchema.Create("migration", ConfigurationFieldKind.Section).WithFields(
            Text("username"),
            FieldSchema.Create("password", ConfigurationFieldKind.SecretReference),
            Duration("lockWait", TimeSpan.Zero, TimeSpan.FromHours(1)).WithDefault(TimeSpan.FromSeconds(60))));

    private static FieldSchema Text(string name) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithLength(1, 256));

    private static FieldSchema Integer(string name, long minimum, long maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(minimum, maximum));

    private static FieldSchema Duration(string name, TimeSpan minimum, TimeSpan maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(minimum, maximum));
}

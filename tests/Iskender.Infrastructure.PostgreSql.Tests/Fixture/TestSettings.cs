using System.Text.Json;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// Builds the <c>iskender.persistence</c> section for a class database, as a configuration document, with
/// <c>tlsMode: disable</c> for the <c>development</c> environment.
/// </summary>
internal static class TestSettings
{
    public static SecretReference RuntimeSecret { get; } = new("test/runtime");

    public static SecretReference MigrationSecret { get; } = new("test/migration");

    /// <summary>A JSON document with the section's required fields, the migration login, and any extra fields.</summary>
    public static string Document(ClassDatabase database, IReadOnlyDictionary<string, object>? overrides = null, bool withMigration = true)
    {
        var section = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["host"] = database.Fixture.Host,
            ["port"] = database.Fixture.Port,
            ["database"] = database.Name,
            ["username"] = PostgreSqlFixture.RuntimeLogin,
            ["password"] = Placeholder(RuntimeSecret),
            ["tlsMode"] = "disable",
        };
        if (withMigration)
        {
            section["migration"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["username"] = PostgreSqlFixture.MigratorLogin,
                ["password"] = Placeholder(MigrationSecret),
            };
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, object>())
        {
            section[key] = value;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["iskender"] = new Dictionary<string, object> { ["persistence"] = section } });
    }

    /// <summary>The ISK-0003 placeholder object, <c>{"secretRef":"&lt;name&gt;"}</c>.</summary>
    public static Dictionary<string, object> Placeholder(SecretReference reference) => new(StringComparer.Ordinal) { ["secretRef"] = reference.Name };
}

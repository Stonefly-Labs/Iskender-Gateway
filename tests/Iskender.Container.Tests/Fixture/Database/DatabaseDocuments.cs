using System.Text.Json;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Container.Tests.Fixture.Database;

/// <summary>The <c>persistence</c> sections the container and hosting tests compose with, as ISK-0004's test settings write them.</summary>
internal static class DatabaseDocuments
{
    public static SecretReference RuntimeSecret { get; } = new("test/runtime");

    public static SecretReference MigrationSecret { get; } = new("test/migration");

    /// <summary>
    /// <c>{"iskender":{"persistence":{...}}}</c> for <paramref name="database"/>: the runtime login, TLS off, which only the
    /// <c>development</c> environment allows, and, with <paramref name="withMigration"/>, the migration login, with
    /// <paramref name="overrides"/> replacing fields.
    /// </summary>
    public static string Document(PlaneDatabase database, IReadOnlyDictionary<string, object>? overrides = null, bool withMigration = true)
    {
        var section = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["host"] = database.Server.Host,
            ["port"] = database.Server.Port,
            ["database"] = database.Name,
            ["username"] = DatabaseServer.RuntimeLogin,
            ["password"] = Placeholder(RuntimeSecret),
            ["tlsMode"] = "disable",
        };
        if (withMigration)
        {
            section["migration"] = Migration();
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, object>())
        {
            section[key] = value;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["iskender"] = new Dictionary<string, object> { ["persistence"] = section } });
    }

    /// <summary>A <c>migration</c> override with the migration login and the given lock wait, such as <c>1s</c>.</summary>
    public static Dictionary<string, object> MigrationWithLockWait(string lockWait) => new(StringComparer.Ordinal)
    {
        ["migration"] = Migration(lockWait),
    };

    private static Dictionary<string, object> Migration(string? lockWait = null)
    {
        var migration = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["username"] = DatabaseServer.MigratorLogin,
            ["password"] = Placeholder(MigrationSecret),
        };
        if (lockWait is not null)
        {
            migration["lockWait"] = lockWait;
        }

        return migration;
    }

    private static Dictionary<string, string> Placeholder(SecretReference reference) => new(StringComparer.Ordinal) { ["secretRef"] = reference.Name };
}

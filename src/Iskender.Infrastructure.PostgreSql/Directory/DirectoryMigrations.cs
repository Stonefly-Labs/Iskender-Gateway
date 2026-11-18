using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Directory;

/// <summary>
/// The migration module <c>directory</c> (persistence contract Migrations rule 12, ISK-0009 research R2): schema
/// <c>isk_directory</c>, supported range <c>[1, 1]</c>, no dependencies, scripts embedded in this assembly.
/// <c>AddIskenderPostgreSqlCoordination()</c> and <c>AddIskenderPostgreSqlWork()</c> add it to the migration set. Owned by ISK-0009.
/// </summary>
public static class DirectoryMigrations
{
    public const string Name = "directory";

    public static MigrationModule Module { get; } = new(
        Name,
        new SchemaVersionRange(1, 1),
        typeof(DirectoryMigrations).Assembly,
        "Iskender.Infrastructure.PostgreSql.Directory.Scripts.");
}

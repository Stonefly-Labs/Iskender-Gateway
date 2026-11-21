using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The migration module <c>work</c> (persistence contract Migrations rule 12, ISK-0009 research R5 and R10): schema
/// <c>isk_work</c>, supported range <c>[1, 1]</c>, depending on <c>directory</c>, scripts embedded in this assembly.
/// <c>AddIskenderPostgreSqlWork()</c> adds it to the migration set. Owned by ISK-0009.
/// </summary>
public static class WorkMigrations
{
    public const string Name = "work";

    public static MigrationModule Module { get; } = new(
        Name,
        new SchemaVersionRange(1, 1),
        typeof(WorkMigrations).Assembly,
        "Iskender.Infrastructure.PostgreSql.Work.Scripts.",
        DirectoryMigrations.Name);
}

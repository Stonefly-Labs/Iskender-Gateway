using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Infrastructure.PostgreSql.Audit;

/// <summary>
/// The migration module <c>audit</c> (persistence contract Migrations rule 12, ISK-0010 research R12 and R15): schema
/// <c>isk_audit</c>, supported range <c>[1, 1]</c>, depending on <c>work</c> for ordering only, scripts embedded in this
/// assembly. <c>AddIskenderPostgreSqlIdentity()</c> adds it to the migration set. Owned by ISK-0010.
/// </summary>
public static class AuditMigrations
{
    public const string Name = "audit";

    public static MigrationModule Module { get; } = new(
        Name,
        new SchemaVersionRange(1, 1),
        typeof(AuditMigrations).Assembly,
        "Iskender.Infrastructure.PostgreSql.Audit.Scripts.",
        WorkMigrations.Name);
}

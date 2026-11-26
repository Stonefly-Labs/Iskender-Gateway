using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The migration module <c>identity</c> (persistence contract Migrations rule 12, ISK-0010 research R3, R4, R11, and
/// R15): schema <c>isk_identity</c>, supported range <c>[1, 1]</c>, depending on <c>audit</c>, scripts embedded in this
/// assembly. <c>AddIskenderPostgreSqlIdentity()</c> adds it to the migration set. Owned by ISK-0010.
/// </summary>
public static class IdentityMigrations
{
    public const string Name = "identity";

    public static MigrationModule Module { get; } = new(
        Name,
        new SchemaVersionRange(1, 1),
        typeof(IdentityMigrations).Assembly,
        "Iskender.Infrastructure.PostgreSql.Identity.Scripts.",
        AuditMigrations.Name);
}

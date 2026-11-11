using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// The adapter's one production migration module, <c>coordination</c> (persistence contract Migrations rule 12, ISK-0005
/// research R8): schema <c>isk_coordination</c>, supported range <c>[1, 1]</c>, no dependencies, scripts embedded in this
/// assembly. A host that uses the PostgreSQL coordinator or the fencing guard adds it to its migration set. Owned by ISK-0005.
/// </summary>
public static class CoordinationMigrations
{
    public const string Name = "coordination";

    public static MigrationModule Module { get; } = new(
        Name,
        new SchemaVersionRange(1, 1),
        typeof(CoordinationMigrations).Assembly,
        "Iskender.Infrastructure.PostgreSql.Coordination.Scripts.");
}

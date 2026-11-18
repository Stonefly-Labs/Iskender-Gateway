using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// The fixture-only <c>applies</c> module (ISK-0009 T011): a scope-keyed table the test handlers write one row per apply
/// to, through the handler's unit. Its scripts are embedded in this test assembly only.
/// </summary>
internal static class AppliesModule
{
    public static MigrationModule Module { get; } = new(
        "applies",
        new SchemaVersionRange(1, 1),
        typeof(AppliesModule).Assembly,
        "Iskender.Work.Tests.Fixture.Applies.");

    public static ScopeKeyedTable Applies { get; } = new("applies", "applies");
}

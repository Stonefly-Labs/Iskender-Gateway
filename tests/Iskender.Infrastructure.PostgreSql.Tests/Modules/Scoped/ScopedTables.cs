namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Scoped;

/// <summary>The scoped module's table descriptors, declared once (ISK-0005 research R6).</summary>
internal static class ScopedTables
{
    public static readonly ScopeKeyedTable Slots = new("scoped", "slots");
}

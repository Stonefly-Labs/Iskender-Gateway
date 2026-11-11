namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>The coordination module's scope-keyed table descriptors, declared once (ISK-0005 data model "Database objects"). Owned by ISK-0005.</summary>
internal static class CoordinationTables
{
    public static readonly ScopeKeyedTable Values = new("coordination", "coordination_values");

    public static readonly ScopeKeyedTable Leases = new("coordination", "leases");

    public static readonly ScopeKeyedTable Fences = new("coordination", "fences");

    public static readonly ScopeKeyedTable CacheGenerations = new("coordination", "cache_generations");
}

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>The work module's scope-keyed table descriptors, declared once (ISK-0009 data model "Database objects"). Owned by ISK-0009.</summary>
internal static class WorkTables
{
    public static readonly ScopeKeyedTable Items = new("work", "items");

    public static readonly ScopeKeyedTable JobRuns = new("work", "job_runs");
}

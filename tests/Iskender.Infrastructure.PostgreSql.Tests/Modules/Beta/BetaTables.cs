namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Beta;

/// <summary>The beta module's table descriptors, declared once (research R6).</summary>
internal static class BetaTables
{
    public static readonly TenantTable Counters = new("beta", "counters");

    public static readonly TenantTable LockLog = new("beta", "lock_log");
}

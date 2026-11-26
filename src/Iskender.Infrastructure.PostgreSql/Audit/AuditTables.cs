namespace Iskender.Infrastructure.PostgreSql.Audit;

/// <summary>The audit module's operator table descriptor, declared once (ISK-0010 data model "isk_audit.records"). Owned by ISK-0010.</summary>
internal static class AuditTables
{
    public static readonly OperatorTable Records = new("audit", "records");
}

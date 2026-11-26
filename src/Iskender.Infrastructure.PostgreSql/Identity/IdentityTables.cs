namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>The identity module's operator table descriptors, declared once (ISK-0010 data model "Database objects"). Owned by ISK-0010.</summary>
internal static class IdentityTables
{
    public static readonly OperatorTable Installation = new("identity", "installation");

    public static readonly OperatorTable Principals = new("identity", "principals");

    public static readonly OperatorTable OperatorGrants = new("identity", "operator_grants");

    public static readonly OperatorTable Tokens = new("identity", "tokens");
}

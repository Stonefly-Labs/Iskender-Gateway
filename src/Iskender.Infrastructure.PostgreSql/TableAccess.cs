using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// The module table a statement uses, declared once by the module (research R6). A unit checks it against its scope
/// before the command exists: the operator scope may not use a <see cref="TenantTable"/>, a tenant scope may not use an
/// <see cref="OperatorTable"/>, and every scope may use a <see cref="ScopeKeyedTable"/>. Owned by ISK-0004; the
/// scope-keyed kind by ISK-0005.
/// </summary>
internal abstract partial class TableAccess
{
    private protected TableAccess(string module, string table)
    {
        if (!Migrations.MigrationModule.IsName(module) || table is null || !TablePattern().IsMatch(table))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Schema = "isk_" + module;
        Table = table;
    }

    /// <summary>The module's schema, <c>isk_&lt;module&gt;</c>.</summary>
    public string Schema { get; }

    public string Table { get; }

    /// <summary>The schema-qualified name, for SQL text.</summary>
    public string QualifiedName => $"{Schema}.{Table}";

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TablePattern();
}

/// <summary>
/// A tenant-owned table, with <c>owner_organization_id</c> and row-level security, and also <c>owner_project_id</c>
/// when <paramref name="projectOwned"/>. Owned by ISK-0004.
/// </summary>
internal sealed class TenantTable(string module, string table, bool projectOwned = false) : TableAccess(module, table)
{
    /// <summary>Whether the table also has <c>owner_project_id</c>, so its owner predicate names the project too.</summary>
    public bool ProjectOwned { get; } = projectOwned;
}

/// <summary>An operator table, with no owner columns, used only in the operator scope. Owned by ISK-0004.</summary>
internal sealed class OperatorTable(string module, string table) : TableAccess(module, table);

/// <summary>
/// A scope-keyed table (persistence contract Isolation rule 8, ISK-0005 research R6), with <c>scope_key text not null</c>
/// and the scope-keyed row-level security policy, usable by every scope. Each scope sees only rows of its exact scope. A
/// statement on it binds <c>$1</c> to the unit's own scope key, never a caller's value, and writes <c>{scope}</c> where the
/// predicate <c>scope_key = $1</c> belongs. Only framework code a contract revision permits uses this kind. Owned by ISK-0005.
/// </summary>
internal sealed class ScopeKeyedTable(string module, string table) : TableAccess(module, table)
{
    /// <summary>The placeholder a statement writes for the scope predicate.</summary>
    public const string ScopePredicate = "{scope}";
}

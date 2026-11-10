using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// The module table a statement uses, declared once by the module (research R6). A unit checks it against its scope
/// before the command exists: the operator scope may not use a <see cref="TenantTable"/>, and a tenant scope may not use
/// an <see cref="OperatorTable"/>. Owned by ISK-0004.
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

/// <summary>A tenant-owned table, with <c>owner_organization_id</c> and row-level security. Owned by ISK-0004.</summary>
internal sealed class TenantTable(string module, string table) : TableAccess(module, table);

/// <summary>An operator table, with no owner columns, used only in the operator scope. Owned by ISK-0004.</summary>
internal sealed class OperatorTable(string module, string table) : TableAccess(module, table);

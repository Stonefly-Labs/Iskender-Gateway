using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>The documented derivation of coordination keys (contract Scope and keys rule 3, research R4). Owned by ISK-0005.</summary>
public static class CoordinationKey
{
    /// <summary>
    /// The scope's canonical form: <c>org:&lt;id&gt;</c>, <c>project:&lt;org id&gt;/&lt;project id&gt;</c>, or
    /// <c>operator</c>. It is stored only in a scope-keyed table's <c>scope_key</c> column, never in a key name.
    /// </summary>
    public static string ScopeKey(OwnerScope scope) => scope switch
    {
        OrganizationScope organization => $"org:{organization.Organization.Value}",
        ProjectScope project => $"project:{project.Organization.Value}/{project.Project.Value}",
        OperatorScope => "operator",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>
    /// The lowercase hex SHA-256 over UTF-8 <c>iskender.coordination.v1</c>, <c>0x00</c>, the scope's canonical form,
    /// <c>0x00</c>, the module, <c>0x00</c>, and the name. An invalid argument fails with <c>core.value.invalid</c>.
    /// </summary>
    public static string Hash(OwnerScope scope, string module, string name)
    {
        var text = $"iskender.coordination.v1\0{ScopeKey(scope)}\0{PortNames.RequireModule(module)}\0{PortNames.RequireOperation(name)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>The documented derivation of advisory lock keys (contract Advisory locks rule 2, research R14). Owned by ISK-0004.</summary>
public static class AdvisoryLockKey
{
    /// <summary>
    /// The migration runner's session lock key: the first 8 bytes, big-endian, of SHA-256 of <c>iskender.migrations.lock.v1</c>
    /// (<c>0x556F05EF3BB45D95</c>).
    /// </summary>
    public const long Migration = 6156145740695756181;

    /// <summary>
    /// The first 8 bytes, as a big-endian signed integer, of SHA-256 over UTF-8 <c>iskender.lock.v1</c>, <c>0x00</c>, the
    /// scope's canonical form (<c>org:&lt;id&gt;</c>, <c>project:&lt;org id&gt;/&lt;project id&gt;</c>, or <c>operator</c>),
    /// <c>0x00</c>, the module, <c>0x00</c>, and the name. An invalid argument fails with <c>core.value.invalid</c>.
    /// </summary>
    public static long Derive(OwnerScope scope, string module, string name)
    {
        var canonical = scope switch
        {
            OrganizationScope organization => $"org:{organization.Organization.Value}",
            ProjectScope project => $"project:{project.Organization.Value}/{project.Project.Value}",
            OperatorScope => "operator",
            _ => throw new GatewayException(ErrorCodes.ValueInvalid),
        };

        var text = $"iskender.lock.v1\0{canonical}\0{PortNames.RequireModule(module)}\0{PortNames.RequireOperation(name)}";
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}

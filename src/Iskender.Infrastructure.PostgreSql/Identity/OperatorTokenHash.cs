using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The stored form of an operator token (operator-security contract, Tokens rule 2, research R4): SHA-256 over UTF-8
/// <c>iskender.operator-token.v1</c>, <c>0x00</c>, the purpose code, <c>0x00</c>, the identifier, <c>0x00</c>, and the
/// secret's 32 bytes. Binding the purpose and identifier into the hash means a row cannot be reused for another of
/// either. <see cref="Verify"/> compares in constant time. Owned by ISK-0010.
/// </summary>
internal static class OperatorTokenHash
{
    public const int Length = 32;

    private static ReadOnlySpan<byte> Domain => "iskender.operator-token.v1"u8;

    /// <summary>The hash of <paramref name="token"/> under the stored <paramref name="purpose"/>.</summary>
    public static byte[] Compute(OperatorTokenPurpose purpose, OperatorTokenText token)
    {
        if (token is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var code = Encoding.UTF8.GetBytes(purpose.ToCode());
        var id = Encoding.UTF8.GetBytes(token.TokenId);
        var input = new byte[Domain.Length + 1 + code.Length + 1 + id.Length + 1 + 32];
        try
        {
            var span = input.AsSpan();
            Domain.CopyTo(span);
            span = span[(Domain.Length + 1)..];
            code.CopyTo(span);
            span = span[(code.Length + 1)..];
            id.CopyTo(span);
            span = span[(id.Length + 1)..];
            token.CopySecretTo(span);
            return SHA256.HashData(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    /// <summary>Whether <paramref name="stored"/> is the hash of <paramref name="token"/> under <paramref name="purpose"/>, compared in constant time.</summary>
    public static bool Verify(OperatorTokenPurpose purpose, OperatorTokenText token, ReadOnlySpan<byte> stored) =>
        stored.Length == Length && CryptographicOperations.FixedTimeEquals(Compute(purpose, token), stored);
}

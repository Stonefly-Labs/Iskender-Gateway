using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>
/// The text of an operator token, <c>isk&lt;p&gt;_&lt;id&gt;_&lt;secret&gt;</c> (operator-security contract, Tokens rule
/// 1, research R4). <c>&lt;p&gt;</c> is <c>b</c>, <c>r</c>, or <c>e</c>. <c>&lt;id&gt;</c> is 16 bytes in 26 characters
/// of lowercase Crockford base32, whose first character is <c>0</c> to <c>7</c> because its two pad bits are zero.
/// <c>&lt;secret&gt;</c> is 32 bytes in 43 characters of unpadded base64url, in its one canonical form. The prefix is
/// informational: the stored purpose decides. The secret is held privately and never appears in <see cref="ToString"/>.
/// Owned by ISK-0010.
/// </summary>
public sealed class OperatorTokenText
{
    internal const int IdLength = 26;
    internal const int SecretByteCount = 32;
    internal const int SecretLength = 43;
    internal const int TextLength = 4 + 1 + IdLength + 1 + SecretLength;

    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    private const int IdByteCount = 16;

    private readonly byte[] _secret;

    private OperatorTokenText(OperatorTokenPurpose purpose, string tokenId, byte[] secret)
    {
        Purpose = purpose;
        TokenId = tokenId;
        _secret = secret;
    }

    /// <summary>The purpose the text's prefix names. Informational: the stored purpose decides.</summary>
    public OperatorTokenPurpose Purpose { get; }

    /// <summary>The 26-character identifier, which is not a secret.</summary>
    public string TokenId { get; }

    /// <summary>A new token of <paramref name="purpose"/>, its ID and secret drawn from <see cref="RandomNumberGenerator"/>.</summary>
    public static OperatorTokenText Generate(OperatorTokenPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Span<byte> id = stackalloc byte[IdByteCount];
        var secret = new byte[SecretByteCount];
        try
        {
            RandomNumberGenerator.Fill(id);
            RandomNumberGenerator.Fill(secret);
        }
        catch (CryptographicException)
        {
            throw new GatewayException(ErrorCodes.RandomSourceFailed);
        }

        return new(purpose, EncodeId(id), secret);
    }

    /// <summary>Reads exactly the token shape. Anything else, including surrounding whitespace, returns <c>false</c>.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out OperatorTokenText? token)
    {
        token = null;
        if (text is null || text.Length != TextLength || !text.StartsWith("isk", StringComparison.Ordinal)
            || text[4] != '_' || text[5 + IdLength] != '_' || !TryReadPrefix(text[3], out var purpose))
        {
            return false;
        }

        var id = text.Substring(5, IdLength);
        if (!IsCanonicalId(id))
        {
            return false;
        }

        var secretText = text.AsSpan(6 + IdLength);
        var secret = new byte[SecretByteCount];
        // The re-encoding check refuses padding and set pad bits, so each secret has exactly one text.
        if (Base64Url.DecodeFromChars(secretText, secret, out var consumed, out var written) != System.Buffers.OperationStatus.Done
            || consumed != secretText.Length || written != SecretByteCount
            || !secretText.SequenceEqual(Base64Url.EncodeToString(secret)))
        {
            return false;
        }

        token = new(purpose, id, secret);
        return true;
    }

    /// <summary>
    /// Copies the secret's 32 bytes into <paramref name="destination"/>, for the store's hash only. A destination of another
    /// length fails with <c>core.value.invalid</c>.
    /// </summary>
    public void CopySecretTo(Span<byte> destination)
    {
        if (destination.Length != SecretByteCount)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _secret.CopyTo(destination);
    }

    /// <summary><c>&lt;purpose&gt; token &lt;id&gt;</c>, never the secret.</summary>
    public override string ToString() => $"{Purpose.ToCode()} token {TokenId}";

    /// <summary>The full text, for <see cref="IssuedOperatorToken.Reveal"/> only.</summary>
    internal string FullText() =>
        $"isk{Purpose.ToPrefixLetter()}_{TokenId}_{Base64Url.EncodeToString(_secret)}";

    private static bool TryReadPrefix(char letter, out OperatorTokenPurpose purpose)
    {
        foreach (var candidate in Enum.GetValues<OperatorTokenPurpose>())
        {
            if (candidate.ToPrefixLetter() == letter)
            {
                purpose = candidate;
                return true;
            }
        }

        purpose = default;
        return false;
    }

    private static bool IsCanonicalId(string id) =>
        id[0] is >= '0' and <= '7' && id.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    private static string EncodeId(ReadOnlySpan<byte> bytes)
    {
        UInt128 value = 0;
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
        }

        return string.Create(IdLength, value, static (chars, v) =>
        {
            for (var i = 0; i < IdLength; i++)
            {
                chars[i] = Alphabet[(int)((v >> (5 * (IdLength - 1 - i))) & 31)];
            }
        });
    }
}

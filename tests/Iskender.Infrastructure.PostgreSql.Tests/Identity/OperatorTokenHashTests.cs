using System.Security.Cryptography;
using System.Text;
using Iskender.Domain.Identity;
using Iskender.Infrastructure.PostgreSql.Identity;

namespace Iskender.Infrastructure.PostgreSql.Tests.Identity;

/// <summary>The token hash of research R4 and the operator-security contract, Tokens rule 2 (ISK-0010 T010, FR-004).</summary>
public class OperatorTokenHashTests
{
    [Fact]
    public void TheHashIsTheContractFormulaOverTheTokensParts()
    {
        var token = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);
        var secret = new byte[32];
        token.CopySecretTo(secret);
        byte[] expected = SHA256.HashData([.. "iskender.operator-token.v1"u8, 0, .. "bootstrap"u8, 0, .. Encoding.UTF8.GetBytes(token.TokenId), 0, .. secret]);

        var hash = OperatorTokenHash.Compute(OperatorTokenPurpose.Bootstrap, token);

        Assert.Equal(expected, hash);
        Assert.Equal(32, hash.Length);
        Assert.True(OperatorTokenHash.Verify(OperatorTokenPurpose.Bootstrap, token, hash));
    }

    [Fact]
    public void AChangeOfPurposeIdOrOneSecretBitChangesTheHash()
    {
        var text = Reveal(OperatorTokenText.Generate(OperatorTokenPurpose.Recovery));
        var token = Parse(text);
        var hash = OperatorTokenHash.Compute(OperatorTokenPurpose.Recovery, token);

        var otherId = Parse(text[..5] + (text[5] == '0' ? '1' : '0') + text[6..]);
        var secret = Convert.FromBase64String(text[32..].Replace('-', '+').Replace('_', '/') + "=");
        secret[0] ^= 1;
        var otherSecret = Parse(text[..32] + Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        Assert.NotEqual(hash, OperatorTokenHash.Compute(OperatorTokenPurpose.Bootstrap, token));
        Assert.NotEqual(hash, OperatorTokenHash.Compute(OperatorTokenPurpose.Recovery, otherId));
        Assert.NotEqual(hash, OperatorTokenHash.Compute(OperatorTokenPurpose.Recovery, otherSecret));
        Assert.False(OperatorTokenHash.Verify(OperatorTokenPurpose.Bootstrap, token, hash));
        Assert.False(OperatorTokenHash.Verify(OperatorTokenPurpose.Recovery, otherSecret, hash));
    }

    [Fact]
    public void AWrongOrShortStoredHashDoesNotVerify()
    {
        var token = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);
        var hash = OperatorTokenHash.Compute(OperatorTokenPurpose.Bootstrap, token);
        var wrong = (byte[])hash.Clone();
        wrong[^1] ^= 0x80;

        Assert.False(OperatorTokenHash.Verify(OperatorTokenPurpose.Bootstrap, token, wrong));
        Assert.False(OperatorTokenHash.Verify(OperatorTokenPurpose.Bootstrap, token, hash.AsSpan(0, 31)));
        Assert.False(OperatorTokenHash.Verify(OperatorTokenPurpose.Bootstrap, token, []));
    }

    private static string Reveal(OperatorTokenText token) => new IssuedOperatorToken(token, DateTimeOffset.UtcNow).Reveal();

    private static OperatorTokenText Parse(string text) => OperatorTokenText.TryParse(text, out var token) ? token : throw new InvalidOperationException("not a token");
}

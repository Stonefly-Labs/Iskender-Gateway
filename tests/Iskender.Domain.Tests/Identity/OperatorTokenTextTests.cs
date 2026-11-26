using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;

namespace Iskender.Domain.Tests.Identity;

/// <summary>The token text of the operator-security contract, Tokens rule 1 (research R4, ISK-0010 T005).</summary>
public class OperatorTokenTextTests
{
    [Theory]
    [InlineData(OperatorTokenPurpose.Bootstrap, 'b')]
    [InlineData(OperatorTokenPurpose.Recovery, 'r')]
    [InlineData(OperatorTokenPurpose.Enrollment, 'e')]
    public void AGeneratedTokenRoundTrips(OperatorTokenPurpose purpose, char letter)
    {
        var text = new IssuedOperatorToken(OperatorTokenText.Generate(purpose), DateTimeOffset.UtcNow).Reveal();

        Assert.Matches($"^isk{letter}_[0-7][0-9a-hjkmnp-tv-z]{{25}}_[A-Za-z0-9_-]{{43}}$", text);
        Assert.True(OperatorTokenText.TryParse(text, out var parsed));
        Assert.Equal(purpose, parsed.Purpose);
        Assert.Equal(text.Substring(5, 26), parsed.TokenId);
        Assert.Equal(text, new IssuedOperatorToken(parsed, DateTimeOffset.UtcNow).Reveal());
    }

    [Fact]
    public void TwoGeneratedSecretsAndIdsDiffer()
    {
        var first = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);
        var second = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);
        var firstSecret = new byte[32];
        var secondSecret = new byte[32];
        first.CopySecretTo(firstSecret);
        second.CopySecretTo(secondSecret);

        Assert.NotEqual(first.TokenId, second.TokenId);
        Assert.NotEqual(firstSecret, secondSecret);
    }

    public static TheoryData<string> Malformed()
    {
        var valid = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal();
        var id = valid.Substring(5, 26);
        var secret = valid[32..];
        var data = new TheoryData<string>
        {
            string.Empty,
            "   ",
            "iskx_" + id + "_" + secret,
            "isb_" + id + "_" + secret,
            "ISKB_" + id + "_" + secret,
            "iskb_" + id[..25] + "A_" + secret,
            "iskb_" + id[..25] + "_" + secret,
            "iskb_" + id + "0_" + secret,
            "iskb_" + "8" + id[1..] + "_" + secret,
            "iskb_" + id + "_" + secret[..42],
            "iskb_" + id + "_" + secret + "A",
            "iskb_" + id + "_" + secret[..42] + "=",
            "iskb_" + id + "_" + secret[..41] + "+A",
            "iskb_" + id + "_" + secret[..41] + "/A",
            "iskb" + id + "_" + secret,
            "iskb_" + id + secret,
            "iskb__" + id + "_" + secret,
            valid + " ",
            " " + valid,
            valid + "\n",
        };

        foreach (var excluded in new[] { 'i', 'l', 'o', 'u' })
        {
            data.Add("iskb_" + id[..25] + excluded + "_" + secret);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AnyOtherShapeIsMalformed(string text)
    {
        Assert.False(OperatorTokenText.TryParse(text, out var token));
        Assert.Null(token);
    }

    [Fact]
    public void NullIsMalformed()
    {
        Assert.False(OperatorTokenText.TryParse(null, out var token));
        Assert.Null(token);
    }

    [Fact]
    public void ASecretWithSetPadBitsIsMalformed()
    {
        var valid = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal();
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last = alphabet.IndexOf(valid[^1], StringComparison.Ordinal);
        var noncanonical = valid[..^1] + alphabet[last | 1];

        Assert.False(OperatorTokenText.TryParse(noncanonical, out _));
    }

    [Fact]
    public void ToStringNeverHoldsTheSecretOrTheFullText()
    {
        var text = OperatorTokenText.Generate(OperatorTokenPurpose.Recovery);
        var issued = new IssuedOperatorToken(text, DateTimeOffset.UtcNow);
        var copy = OperatorTokenText.TryParse(new IssuedOperatorToken(text, DateTimeOffset.UtcNow).Reveal(), out var parsed) ? parsed : null;
        var full = new IssuedOperatorToken(copy!, DateTimeOffset.UtcNow).Reveal();
        var secret = full[32..];

        Assert.Equal($"recovery token {text.TokenId}", text.ToString());
        Assert.Equal($"recovery token {text.TokenId}", issued.ToString());
        Assert.DoesNotContain(secret, text.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, issued.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(full, issued.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRevealFailsWithCoreInternal()
    {
        var issued = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow);

        Assert.NotEmpty(issued.Reveal());
        var exception = Assert.Throws<GatewayException>(() => issued.Reveal());
        Assert.Same(ErrorCodes.Internal, exception.Code);
    }

    [Fact]
    public void CopySecretToTakesExactly32Bytes()
    {
        var text = OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap);

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => text.CopySecretTo(new byte[31])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => text.CopySecretTo(new byte[33])).Code);
    }

    [Fact]
    public void AnIssuedTokenNeedsATextAndAnExpiry()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new IssuedOperatorToken(null!, DateTimeOffset.UtcNow)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), default)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow, -1)).Code);
        Assert.Equal(2, new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow, 2).Superseded);
    }
}

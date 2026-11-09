using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Core.Contracts.Tests.Configuration;

public class SecretReferenceTests
{
    private const string Canary = "ISKENDER-CANARY-7f3a";

    [Theory]
    [InlineData("a")]
    [InlineData("openai/primary")]
    [InlineData("A.b_c-d/E9")]
    [InlineData("a..b")]
    [InlineData("a/.b/c.")]
    [InlineData("...")]
    public void AcceptsValidNames(string name)
    {
        var reference = new SecretReference(name);

        Assert.Equal(name, reference.Name);
        Assert.True(SecretReference.IsValidName(name));
    }

    [Fact]
    public void AcceptsTheBoundaryLengths()
    {
        Assert.Equal(1, new SecretReference("x").Name.Length);
        Assert.Equal(256, new SecretReference(new string('x', 256)).Name.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a@b")]
    [InlineData("a+b")]
    [InlineData("é")]
    [InlineData("a\u0000b")]
    [InlineData("/a")]
    [InlineData("a/")]
    [InlineData("/")]
    [InlineData("a//b")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("../a")]
    [InlineData("a/..")]
    public void RejectsMalformedNames(string name)
    {
        Assert.False(SecretReference.IsValidName(name));
        Assert.Equal(ErrorCodes.ConfigSecretReferenceMalformed, Assert.Throws<GatewayException>(() => new SecretReference(name)).Code);
    }

    [Fact]
    public void RejectsANameOf257CharactersAndNull()
    {
        Assert.Throws<GatewayException>(() => new SecretReference(new string('x', 257)));
        Assert.Throws<GatewayException>(() => new SecretReference(null!));
        Assert.False(SecretReference.IsValidName(null));
    }

    [Fact]
    public void TheFailureHoldsNoPartOfTheName()
    {
        var exception = Assert.Throws<GatewayException>(() => new SecretReference(Canary + "/../x"));

        Assert.DoesNotContain(Canary, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.Error);
    }

    [Fact]
    public void RendersAsSecretRefAndName()
    {
        Assert.Equal("secretRef:openai/primary", new SecretReference("openai/primary").ToString());
    }

    [Fact]
    public void HasOrdinalValueEquality()
    {
        Assert.Equal(new SecretReference("a/b"), new SecretReference("a/b"));
        Assert.NotEqual(new SecretReference("a/b"), new SecretReference("A/b"));
        Assert.True(new SecretReference("a") == new SecretReference("a"));
        Assert.Equal(new SecretReference("a/b").GetHashCode(), new SecretReference("a/b").GetHashCode());
    }
}

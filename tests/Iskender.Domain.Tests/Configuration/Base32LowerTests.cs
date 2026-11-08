using System.Text;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class Base32LowerTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "my")]
    [InlineData("fo", "mzxq")]
    [InlineData("foo", "mzxw6")]
    [InlineData("foob", "mzxw6yq")]
    [InlineData("fooba", "mzxw6ytb")]
    [InlineData("foobar", "mzxw6ytboi")]
    public void EncodesTheRfc4648Section10VectorsLowerCasedAndUnpadded(string input, string expected)
    {
        Assert.Equal(expected, Base32Lower.Encode(Encoding.ASCII.GetBytes(input)));
    }

    [Fact]
    public void AShaTwoFiftySixHashIsFiftyTwoCharactersOfTheLowerAlphabet()
    {
        var text = Base32Lower.Encode(new byte[32]);

        Assert.Equal(52, text.Length);
        Assert.All(Base32Lower.Encode(Enumerable.Range(0, 32).Select(i => (byte)(i * 37)).ToArray()), c => Assert.True(c is (>= 'a' and <= 'z') or (>= '2' and <= '7')));
    }
}

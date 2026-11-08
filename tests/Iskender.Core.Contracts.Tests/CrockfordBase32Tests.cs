using System.Security.Cryptography;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class CrockfordBase32Tests
{
    [Fact]
    public void AllZeroBytesEncodeToTwentySixZeros()
    {
        Assert.Equal(new string('0', 26), CrockfordBase32.Encode(new byte[16]));
    }

    [Fact]
    public void AllOnesEncodeToSevenThenTwentyFiveZs()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, 16).ToArray();

        Assert.Equal("7" + new string('z', 25), CrockfordBase32.Encode(bytes));
    }

    [Fact]
    public void LowBitEncodesInTheLastCharacter()
    {
        var bytes = new byte[16];
        bytes[15] = 1;

        Assert.Equal(new string('0', 25) + "1", CrockfordBase32.Encode(bytes));
    }

    [Fact]
    public void RandomValuesRoundTrip()
    {
        for (var i = 0; i < 1000; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            var text = CrockfordBase32.Encode(bytes);

            Assert.Equal(26, text.Length);
            Assert.Equal(CrockfordBase32.BodyCheck.Canonical, CrockfordBase32.Check(text));
            Assert.Equal(bytes, CrockfordBase32.Decode(text));
        }
    }

    [Theory]
    [InlineData("0000000000000000000000000A")] // upper case
    [InlineData("0000000000000000000000000i")] // outside the alphabet
    [InlineData("0000000000000000000000000l")]
    [InlineData("0000000000000000000000000o")]
    [InlineData("0000000000000000000000000u")]
    [InlineData("000000000000000000000000-0")]
    [InlineData("80000000000000000000000000")] // a pad bit set
    [InlineData("z0000000000000000000000000")]
    [InlineData("000000000000000000000000000")] // 27 characters
    [InlineData("0000000000000000000000000")] // 25 characters
    public void NonCanonicalBodiesAreReported(string body)
    {
        Assert.Equal(CrockfordBase32.BodyCheck.NonCanonical, CrockfordBase32.Check(body));
    }
}

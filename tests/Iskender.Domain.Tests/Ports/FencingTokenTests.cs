using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class FencingTokenTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void AZeroOrNegativeValueIsRejected(long value)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FencingToken(value)).Code);
    }

    [Fact]
    public void TokensAreOrderedByValue()
    {
        var one = new FencingToken(1);
        var two = new FencingToken(2);

        Assert.True(one < two);
        Assert.True(two > one);
        Assert.True(one <= new FencingToken(1));
        Assert.True(two >= new FencingToken(2));
        Assert.False(two <= one);
        Assert.Equal(-1, one.CompareTo(two));
        Assert.Equal([one, two, new FencingToken(long.MaxValue)], new[] { new FencingToken(long.MaxValue), two, one }.Order());
    }

    [Fact]
    public void TokensHaveValueEquality()
    {
        Assert.Equal(new FencingToken(5), new FencingToken(5));
        Assert.NotEqual(new FencingToken(5), new FencingToken(6));
        Assert.Equal("5", new FencingToken(5).ToString());
    }
}

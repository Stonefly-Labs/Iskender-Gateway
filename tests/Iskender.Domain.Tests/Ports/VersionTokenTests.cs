using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class VersionTokenTests
{
    [Fact]
    public void InitialIsOneAndNextAddsOne()
    {
        Assert.Equal(1, VersionToken.Initial.Value);
        Assert.Equal(new VersionToken(2), VersionToken.Initial.Next());
        Assert.Equal(43, new VersionToken(42).Next().Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void ZeroOrANegativeValueIsRejected(long value)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new VersionToken(value)).Code);
    }

    [Fact]
    public void TokensCompareByValue()
    {
        Assert.Equal(new VersionToken(5), new VersionToken(5));
        Assert.NotEqual(new VersionToken(5), new VersionToken(6));
    }
}

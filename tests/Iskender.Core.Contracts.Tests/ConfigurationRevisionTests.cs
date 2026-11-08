using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Core.Contracts.Tests;

public class ConfigurationRevisionTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("rev-2026.11.07")]
    [InlineData("0123456789abcdefghijklmnopqrstuvwxyz.-")]
    public void ValidValuesAreKept(string value)
    {
        Assert.Equal(value, new ConfigurationRevision(value).Value);
    }

    [Fact]
    public void SixtyFourCharactersAreAccepted()
    {
        Assert.Equal(64, new ConfigurationRevision(new string('a', 64)).Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Rev1")]
    [InlineData("rev_1")]
    [InlineData("rev 1")]
    [InlineData("rev\n1")]
    [InlineData("révision")]
    public void InvalidValuesFailWithValueInvalid(string? value)
    {
        var exception = Assert.Throws<GatewayException>(() => new ConfigurationRevision(value!));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    [Fact]
    public void SixtyFiveCharactersAreRejected()
    {
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new ConfigurationRevision(new string('a', 65))).Code.Id);
    }

    [Fact]
    public void EqualityIsOrdinalOnTheValue()
    {
        Assert.True(new ConfigurationRevision("r1") == new ConfigurationRevision("r1"));
        Assert.True(new ConfigurationRevision("r1") != new ConfigurationRevision("r2"));
    }
}

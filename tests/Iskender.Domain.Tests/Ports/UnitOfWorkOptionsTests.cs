using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class UnitOfWorkOptionsTests
{
    [Fact]
    public void DefaultsAreReadCommittedSixtySecondsNoDeadlineAndNotSafeToRerun()
    {
        var options = new UnitOfWorkOptions("alpha", "widgets.update");

        Assert.Equal("alpha", options.Module);
        Assert.Equal("widgets.update", options.Operation);
        Assert.Equal(UnitOfWorkIsolation.ReadCommitted, options.Isolation);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Timeout);
        Assert.Null(options.Deadline);
        Assert.False(options.SafeToRerun);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a_1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz01234")]
    public void AcceptsModuleNamesAtTheirBounds(string module)
    {
        Assert.Equal(module, new UnitOfWorkOptions(module, "op").Module);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1a")]
    [InlineData("_a")]
    [InlineData("Alpha")]
    [InlineData("al.pha")]
    [InlineData("al-pha")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("alpha\n")]
    [InlineData(null)]
    public void RejectsModuleNamesOutsideThePattern(string? module)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new UnitOfWorkOptions(module!, "op")).Code);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("registry.update")]
    [InlineData("a_b.c_1")]
    public void AcceptsOperationNames(string operation)
    {
        Assert.Equal(operation, new UnitOfWorkOptions("alpha", operation).Operation);
    }

    [Fact]
    public void AcceptsAnOperationOfSixtyThreeCharactersAndRejectsSixtyFour()
    {
        var longest = "a" + new string('b', 62);

        Assert.Equal(longest, new UnitOfWorkOptions("alpha", longest).Operation);
        Assert.Throws<GatewayException>(() => new UnitOfWorkOptions("alpha", longest + "c"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".a")]
    [InlineData("1a")]
    [InlineData("Registry.update")]
    [InlineData("a-b")]
    [InlineData(null)]
    public void RejectsOperationNamesOutsideThePattern(string? operation)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new UnitOfWorkOptions("alpha", operation!)).Code);
    }

    [Fact]
    public void ATimeoutMustBeMoreThanZero()
    {
        var options = new UnitOfWorkOptions("alpha", "op");

        Assert.Equal(TimeSpan.FromTicks(1), (options with { Timeout = TimeSpan.FromTicks(1) }).Timeout);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => options with { Timeout = TimeSpan.Zero }).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => options with { Timeout = TimeSpan.FromSeconds(-1) }).Code);
    }

    [Fact]
    public void AnUndefinedIsolationIsRejected()
    {
        Assert.Throws<GatewayException>(() => new UnitOfWorkOptions("alpha", "op") { Isolation = (UnitOfWorkIsolation)7 });
        Assert.Equal(UnitOfWorkIsolation.Serializable, new UnitOfWorkOptions("alpha", "op") { Isolation = UnitOfWorkIsolation.Serializable }.Isolation);
    }
}

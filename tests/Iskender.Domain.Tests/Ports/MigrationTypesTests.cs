using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class MigrationTypesTests
{
    [Fact]
    public void IdentitiesCompareByModuleAndVersion()
    {
        Assert.Equal(new MigrationIdentity("alpha", 3), new MigrationIdentity("alpha", 3));
        Assert.NotEqual(new MigrationIdentity("alpha", 3), new MigrationIdentity("beta", 3));
        Assert.NotEqual(new MigrationIdentity("alpha", 3), new MigrationIdentity("alpha", 4));
        Assert.Equal("alpha/3", new MigrationIdentity("alpha", 3).ToString());
    }

    [Theory]
    [InlineData("alpha", 0)]
    [InlineData("alpha", -1)]
    [InlineData("Alpha", 1)]
    [InlineData("", 1)]
    public void AnIdentityNeedsAModuleNameAndAPositiveVersion(string module, int version)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new MigrationIdentity(module, version)).Code);
    }

    [Fact]
    public void AReverseRequestDefaultsToOneUnconfirmedStep()
    {
        var request = new MigrationReverseRequest();

        Assert.Equal(1, request.Steps);
        Assert.False(request.ConfirmDataLoss);
        Assert.Equal(7, (request with { Steps = 7 }).Steps);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => request with { Steps = 0 }).Code);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    [InlineData(3, 9)]
    public void ARangeAcceptsPositiveOrderedBounds(int minimum, int maximum)
    {
        var range = new SchemaVersionRange(minimum, maximum);

        Assert.Equal(minimum, range.Minimum);
        Assert.Equal(maximum, range.Maximum);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(-1, -1)]
    public void ARangeRejectsZeroOrReversedBounds(int minimum, int maximum)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new SchemaVersionRange(minimum, maximum)).Code);
    }

    [Fact]
    public void AStatusAndAResultCopyTheirLists()
    {
        var pending = new List<MigrationIdentity> { new("alpha", 4) };
        var status = new MigrationStatus("alpha", 3, new SchemaVersionRange(1, 5), pending, [], []);
        var result = new MigrationRunResult(pending, []);
        pending.Clear();

        Assert.Equal(3, status.SchemaVersion);
        Assert.Equal([new MigrationIdentity("alpha", 4)], status.Pending);
        Assert.Equal([new MigrationIdentity("alpha", 4)], result.Applied);
        Assert.Empty(result.Reversed);
    }
}

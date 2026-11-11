using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class LeaseWaitTests
{
    [Fact]
    public void TryOnlyHasNoBound()
    {
        Assert.Null(LeaseWait.TryOnly.Bound);
    }

    [Fact]
    public void ABoundedWaitKeepsItsBound()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1), LeaseWait.Bounded(TimeSpan.FromMilliseconds(1)).Bound);
        Assert.Equal(TimeSpan.FromMinutes(5), LeaseWait.Bounded(TimeSpan.FromMinutes(5)).Bound);
    }

    [Fact]
    public void AZeroOrNegativeBoundIsRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => LeaseWait.Bounded(TimeSpan.Zero)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => LeaseWait.Bounded(TimeSpan.FromTicks(-1))).Code);
    }
}

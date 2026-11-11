using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class AdvisoryLockNameTests
{
    [Fact]
    public void NamesCompareByValue()
    {
        Assert.Equal(new AdvisoryLockName("beta", "log.append"), new AdvisoryLockName("beta", "log.append"));
        Assert.NotEqual(new AdvisoryLockName("beta", "log.append"), new AdvisoryLockName("alpha", "log.append"));
        Assert.NotEqual(new AdvisoryLockName("beta", "log.append"), new AdvisoryLockName("beta", "log"));
    }

    [Theory]
    [InlineData("1beta", "log")]
    [InlineData("be.ta", "log")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "log")]
    [InlineData("beta", ".log")]
    [InlineData("beta", "log-append")]
    [InlineData("beta", null)]
    [InlineData(null, "log")]
    public void RejectsNamesOutsideTheirPatterns(string? module, string? name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new AdvisoryLockName(module!, name!)).Code);
    }

    [Fact]
    public void ABoundedWaitMustBePositive()
    {
        Assert.Null(AdvisoryLockWait.TryOnly.Bound);
        Assert.Equal(TimeSpan.FromSeconds(1), AdvisoryLockWait.Bounded(TimeSpan.FromSeconds(1)).Bound);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => AdvisoryLockWait.Bounded(TimeSpan.Zero)).Code);
    }

    [Fact]
    public void AFailureRecordsItsCodeClassAndCommitOutcome()
    {
        var failure = new PersistenceFailure(ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.Unavailable, commitOutcomeUnknown: true);

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
        Assert.Equal(PersistenceFailureClass.Unavailable, failure.FailureClass);
        Assert.True(failure.CommitOutcomeUnknown);
        Assert.False(new PersistenceFailure(ErrorCodes.Internal, PersistenceFailureClass.Unexpected).CommitOutcomeUnknown);
        Assert.Throws<GatewayException>(() => new PersistenceFailure(null!, PersistenceFailureClass.Unexpected));
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationFailureClassTests
{
    [Fact]
    public void EachClassHasItsFailureClassValue()
    {
        Assert.Equal(
            ["unavailable", "timeout", "cancelled", "lease_lost", "fencing_rejected", "capability_unsupported", "scope_violation", "authentication_failed", "unexpected"],
            Enum.GetValues<CoordinationFailureClass>().Select(failureClass => failureClass.ToCode()));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((CoordinationFailureClass)99).ToCode()).Code);
    }

    [Fact]
    public void ARaisedFailureCarriesOnlyItsCodeClassAndOutcome()
    {
        var failure = CoordinationFailures.Raise(ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.AuthenticationFailed, outcomeUnknown: true);

        Assert.Same(ErrorCodes.CoordinationUnavailable, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(
            [new("failure_class", "authentication_failed"), new KeyValuePair<string, string>("outcome", "unknown")],
            failure.Error!.Diagnostics.Values);
        Assert.Null(failure.Error.Diagnostics.Text);
        Assert.Equal(CoordinationFailureClass.AuthenticationFailed, CoordinationFailures.ClassOf(failure));
        Assert.True(CoordinationFailures.IsOutcomeUnknown(failure));
    }

    [Fact]
    public void AKnownOutcomeCarriesNoOutcomeValue()
    {
        var failure = CoordinationFailures.Raise(ErrorCodes.CoordinationTimeout, CoordinationFailureClass.Timeout);

        Assert.Single(failure.Error!.Diagnostics.Values);
        Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));
        Assert.Equal(CoordinationFailureClass.Timeout, CoordinationFailures.ClassOf(failure));
    }

    [Fact]
    public void AnExceptionWithoutAClassHasNone()
    {
        Assert.Null(CoordinationFailures.ClassOf(new GatewayException(ErrorCodes.Internal)));
        Assert.False(CoordinationFailures.IsOutcomeUnknown(new GatewayException(ErrorCodes.Internal)));
    }
}

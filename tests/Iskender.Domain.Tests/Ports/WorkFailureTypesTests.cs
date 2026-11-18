using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class WorkFailureTypesTests
{
    [Fact]
    public void PermanentCarriesItsCodeAndThePermanentMarker()
    {
        var failure = WorkFailures.Permanent(ErrorCodes.WorkPayloadInvalid);

        Assert.Same(ErrorCodes.WorkPayloadInvalid, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("work_failure", "permanent"), failure.Error!.Diagnostics.Values);
        Assert.True(WorkFailures.IsPermanent(failure));
    }

    [Fact]
    public void AnyOtherFailureIsNotPermanent()
    {
        Assert.False(WorkFailures.IsPermanent(new GatewayException(ErrorCodes.WorkPayloadInvalid)));
        Assert.False(WorkFailures.IsPermanent(CoordinationFailures.Raise(ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable)));
    }

    [Fact]
    public void TheFailureClassCodesAreFixed()
    {
        Assert.Equal(
            ["transient", "permanent", "unclassified", "abandoned"],
            Enum.GetValues<WorkFailureClass>().Select(failureClass => failureClass.ToCode()));
    }

    [Fact]
    public void TheStateCodesAreFixed()
    {
        Assert.Equal(["pending", "completed", "poisoned"], Enum.GetValues<WorkItemState>().Select(state => state.ToCode()));
    }

    [Fact]
    public void StoredCodesParseBackAndNothingElseDoes()
    {
        Assert.All(Enum.GetValues<WorkFailureClass>(), failureClass => Assert.True(WorkFailureClasses.TryParse(failureClass.ToCode(), out var parsed) && parsed == failureClass));
        Assert.All(Enum.GetValues<WorkItemState>(), state => Assert.True(WorkItemStates.TryParse(state.ToCode(), out var parsed) && parsed == state));
        Assert.False(WorkFailureClasses.TryParse("Transient", out _));
        Assert.False(WorkItemStates.TryParse(null, out _));
    }

    [Fact]
    public void AnUndefinedValueHasNoCode()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((WorkFailureClass)99).ToCode()).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((WorkItemState)99).ToCode()).Code);
    }
}

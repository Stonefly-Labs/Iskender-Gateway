using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;

namespace Iskender.Work.Tests.Retry;

/// <summary>Failure classification, exactly <c>work</c> contract Retry and poison rule 1 (ISK-0009 T016, FR-016).</summary>
public class WorkClassifierTests
{
    [Theory]
    [InlineData("work.payload_invalid")]
    [InlineData("persistence.unavailable")]
    [InlineData("core.value.invalid")]
    public void APermanentFailureIsPermanentWithItsCode(string id)
    {
        var code = Code(id);

        Assert.Equal(new WorkFailure(WorkFailureClass.Permanent, code), WorkClassifier.Classify(WorkFailures.Permanent(code)));
    }

    private static readonly string[] TransientPersistenceCodes = ["persistence.unavailable", "persistence.timeout", "persistence.concurrency_conflict"];

    public static TheoryData<string> TransientCodes() =>
        [.. TransientPersistenceCodes
            .Concat(ErrorCodes.All.Where(code => code.Id.StartsWith("coordination.", StringComparison.Ordinal) && code.Id != "coordination.capability_unsupported").Select(code => code.Id))];

    [Theory]
    [MemberData(nameof(TransientCodes))]
    public void TheTransientCodesAreTransientWithTheirCode(string id)
    {
        var code = Code(id);

        Assert.Equal(new WorkFailure(WorkFailureClass.Transient, code), WorkClassifier.Classify(new GatewayException(code)));
    }

    [Fact]
    public void EveryCoordinationCodeButCapabilityUnsupportedIsTransient()
    {
        Assert.Contains("coordination.lease_lost", TransientCodes().Select(row => row.Data));
        Assert.DoesNotContain("coordination.capability_unsupported", TransientCodes().Select(row => row.Data));
    }

    [Theory]
    [InlineData("coordination.capability_unsupported")]
    [InlineData("persistence.unique_conflict")]
    [InlineData("persistence.scope_not_permitted")]
    [InlineData("persistence.cancelled")]
    [InlineData("core.internal")]
    [InlineData("core.value.invalid")]
    [InlineData("work.payload_invalid")]
    public void AnyOtherCodeIsUnclassifiedWithCoreInternal(string id)
    {
        Assert.Equal(new WorkFailure(WorkFailureClass.Unclassified, ErrorCodes.Internal), WorkClassifier.Classify(new GatewayException(Code(id))));
    }

    [Fact]
    public void AnyOtherExceptionIsUnclassifiedWithCoreInternal()
    {
        foreach (var exception in new Exception[] { new InvalidOperationException(), new OperationCanceledException(), new TimeoutException() })
        {
            Assert.Equal(new WorkFailure(WorkFailureClass.Unclassified, ErrorCodes.Internal), WorkClassifier.Classify(exception));
        }
    }

    [Fact]
    public void TimeoutShutdownAndAbandonmentHaveTheirFixedClassAndCode()
    {
        Assert.Equal(new WorkFailure(WorkFailureClass.Transient, ErrorCodes.WorkHandlerTimeout), WorkClassifier.Timeout);
        Assert.Equal(new WorkFailure(WorkFailureClass.Transient, ErrorCodes.WorkHandlerCancelled), WorkClassifier.Shutdown);
        Assert.Equal(new WorkFailure(WorkFailureClass.Abandoned, ErrorCodes.WorkAttemptAbandoned), WorkClassifier.Abandoned);
    }

    private static ErrorCode Code(string id) => ErrorCodes.All.Single(code => code.Id == id);
}

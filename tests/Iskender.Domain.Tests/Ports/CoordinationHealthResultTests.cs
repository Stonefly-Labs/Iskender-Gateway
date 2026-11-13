using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationHealthResultTests
{
    [Fact]
    public void EachReasonHasItsCode()
    {
        Assert.Equal(
            ["ok", "not_configured", "unreachable", "authentication_failed", "timeout", "read_only", "unexpected"],
            Enum.GetValues<CoordinationHealthReason>().Select(reason => reason.ToCode()));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((CoordinationHealthReason)99).ToCode()).Code);
    }

    [Fact]
    public void HealthyPairsOnlyWithOkOrNotConfigured()
    {
        Assert.Equal(CoordinationHealthReason.Ok, CoordinationHealthResult.Healthy.Reason);
        Assert.Equal(CoordinationHealthReason.NotConfigured, CoordinationHealthResult.NothingDistributed.Reason);
        Assert.Equal(CoordinationHealthStatus.Healthy, CoordinationHealthResult.NothingDistributed.Status);

        foreach (var reason in Enum.GetValues<CoordinationHealthReason>().Except([CoordinationHealthReason.Ok, CoordinationHealthReason.NotConfigured]))
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinationHealthResult(CoordinationHealthStatus.Healthy, reason)).Code);
        }
    }

    [Fact]
    public void OkPairsOnlyWithHealthy()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Ok)).Code);
    }

    [Fact]
    public void EveryOtherReasonCanBeUnhealthyIncludingNotConfigured()
    {
        foreach (var reason in Enum.GetValues<CoordinationHealthReason>().Except([CoordinationHealthReason.Ok]))
        {
            var result = CoordinationHealthResult.Unhealthy(reason);

            Assert.Equal(CoordinationHealthStatus.Unhealthy, result.Status);
            Assert.Equal(reason, result.Reason);
        }
    }

    [Fact]
    public void UndefinedValuesAreRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinationHealthResult((CoordinationHealthStatus)9, CoordinationHealthReason.Unexpected)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CoordinationHealthResult.Unhealthy((CoordinationHealthReason)99)).Code);
    }
}

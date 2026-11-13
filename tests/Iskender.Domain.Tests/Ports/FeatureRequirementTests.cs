using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class FeatureRequirementTests
{
    private static readonly Dictionary<CoordinationPrimitive, ConsistencyLevel> Leased = new() { [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased };

    [Fact]
    public void AValidDeclarationKeepsItsValues()
    {
        var requirement = new FeatureRequirement("quota.monthly", "quota", Leased, mandatoryPath: true, OutageBehavior.FailClosed);

        Assert.Equal("quota.monthly", requirement.Name);
        Assert.Equal("quota", requirement.Module);
        Assert.Equal(ConsistencyLevel.Leased, Assert.Single(requirement.Minimums).Value);
        Assert.True(requirement.MandatoryPath);
        Assert.Same(OutageBehavior.FailClosed, requirement.Outage);
        Assert.True(requirement.Uses(CoordinationPrimitive.Leases));
        Assert.False(requirement.Uses(CoordinationPrimitive.Fencing));
    }

    [Fact]
    public void TheMinimumsAreCopied()
    {
        var minimums = new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased };
        var requirement = new FeatureRequirement("a", "b", minimums, mandatoryPath: false, OutageBehavior.FailClosed);

        minimums[CoordinationPrimitive.Fencing] = ConsistencyLevel.Strict;

        Assert.Single(requirement.Minimums);
    }

    [Fact]
    public void EmptyMinimumsAreAMalformedDeclaration()
    {
        var failure = Assert.Throws<GatewayException>(() => new FeatureRequirement("a", "b", new Dictionary<CoordinationPrimitive, ConsistencyLevel>(), mandatoryPath: false, OutageBehavior.FailClosed));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);

        Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
    }

    [Theory]
    [InlineData("Quota", "quota")]
    [InlineData("quota", "Quota")]
    [InlineData("", "quota")]
    [InlineData("quota", "")]
    [InlineData("quota", "quo.ta")]
    [InlineData("1quota", "quota")]
    public void InvalidNamesAndModulesAreRejected(string name, string module)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FeatureRequirement(name, module, Leased, mandatoryPath: false, OutageBehavior.FailClosed)).Code);
    }

    [Fact]
    public void UndefinedPrimitivesLevelsAndNullsAreRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FeatureRequirement("a", "b", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [(CoordinationPrimitive)9] = ConsistencyLevel.Leased }, false, OutageBehavior.FailClosed)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FeatureRequirement("a", "b", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = (ConsistencyLevel)9 }, false, OutageBehavior.FailClosed)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FeatureRequirement("a", "b", null!, false, OutageBehavior.FailClosed)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new FeatureRequirement("a", "b", Leased, false, null!)).Code);
    }

    [Fact]
    public void ADegradeSignalHasTheOperationPattern()
    {
        var degrade = OutageBehavior.Degrade("quota.degraded");

        Assert.Equal("quota.degraded", degrade.Signal);
        Assert.True(degrade.Degrades);
        Assert.False(OutageBehavior.FailClosed.Degrades);
        Assert.Null(OutageBehavior.FailClosed.Signal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Degraded")]
    [InlineData("quota-degraded")]
    [InlineData(".degraded")]
    [InlineData(null)]
    public void AnInvalidDegradeSignalIsRejected(string? signal)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => OutageBehavior.Degrade(signal!)).Code);
    }
}

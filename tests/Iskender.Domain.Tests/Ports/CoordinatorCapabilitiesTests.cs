using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinatorCapabilitiesTests
{
    // Contract Consistency rule 2, row by row: atomic operations, leases, fencing (null when not offered), cache invalidation.
    public static TheoryData<CoordinatorKind, ConsistencyLevel, ConsistencyLevel, ConsistencyLevel?, ConsistencyLevel> ContractTable => new()
    {
        { CoordinatorKind.Local, ConsistencyLevel.Local, ConsistencyLevel.Local, null, ConsistencyLevel.Local },
        { CoordinatorKind.Redis, ConsistencyLevel.Leased, ConsistencyLevel.Leased, null, ConsistencyLevel.Leased },
        { CoordinatorKind.PostgreSql, ConsistencyLevel.Strict, ConsistencyLevel.Strict, ConsistencyLevel.Strict, ConsistencyLevel.Strict },
    };

    [Theory]
    [MemberData(nameof(ContractTable))]
    public void TheShippedDeclarationsMatchTheContractTable(CoordinatorKind kind, ConsistencyLevel atomic, ConsistencyLevel leases, ConsistencyLevel? fencing, ConsistencyLevel cache)
    {
        var capabilities = kind switch
        {
            CoordinatorKind.Local => CoordinatorCapabilities.Local,
            CoordinatorKind.Redis => CoordinatorCapabilities.Redis,
            _ => CoordinatorCapabilities.PostgreSql,
        };

        Assert.Equal(kind, capabilities.Kind);
        Assert.Equal(atomic, capabilities.LevelOf(CoordinationPrimitive.AtomicOperations));
        Assert.Equal(leases, capabilities.LevelOf(CoordinationPrimitive.Leases));
        Assert.Equal(fencing, capabilities.LevelOf(CoordinationPrimitive.Fencing));
        Assert.Equal(cache, capabilities.LevelOf(CoordinationPrimitive.CacheInvalidation));
    }

    [Fact]
    public void APrimitiveNotDeclaredIsNotOffered()
    {
        var capabilities = new CoordinatorCapabilities(CoordinatorKind.Redis, new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased });

        Assert.Null(capabilities.LevelOf(CoordinationPrimitive.Fencing));
        Assert.Equal(ConsistencyLevel.Leased, capabilities.LevelOf(CoordinationPrimitive.Leases));
    }

    [Fact]
    public void CapabilitiesCompareByKindAndLevels()
    {
        var copy = new CoordinatorCapabilities(CoordinatorKind.Redis, new Dictionary<CoordinationPrimitive, ConsistencyLevel>
        {
            [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased,
            [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased,
            [CoordinationPrimitive.CacheInvalidation] = ConsistencyLevel.Leased,
        });

        Assert.Equal(CoordinatorCapabilities.Redis, copy);
        Assert.Equal(CoordinatorCapabilities.Redis.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(CoordinatorCapabilities.Redis, CoordinatorCapabilities.PostgreSql);
    }

    [Fact]
    public void UndefinedValuesAreRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorCapabilities((CoordinatorKind)9, new Dictionary<CoordinationPrimitive, ConsistencyLevel>())).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorCapabilities(CoordinatorKind.Local, null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorCapabilities(CoordinatorKind.Local, new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = (ConsistencyLevel)9 })).Code);
    }
}

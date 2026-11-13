using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class ConsistencyLevelTests
{
    // Rows are the declared level, columns the required minimum: local, best_effort, leased, strict.
    public static TheoryData<ConsistencyLevel, ConsistencyLevel, bool> Table => new()
    {
        { ConsistencyLevel.Local, ConsistencyLevel.Local, true },
        { ConsistencyLevel.Local, ConsistencyLevel.BestEffort, false },
        { ConsistencyLevel.Local, ConsistencyLevel.Leased, false },
        { ConsistencyLevel.Local, ConsistencyLevel.Strict, false },
        { ConsistencyLevel.BestEffort, ConsistencyLevel.Local, true },
        { ConsistencyLevel.BestEffort, ConsistencyLevel.BestEffort, true },
        { ConsistencyLevel.BestEffort, ConsistencyLevel.Leased, false },
        { ConsistencyLevel.BestEffort, ConsistencyLevel.Strict, false },
        { ConsistencyLevel.Leased, ConsistencyLevel.Local, true },
        { ConsistencyLevel.Leased, ConsistencyLevel.BestEffort, true },
        { ConsistencyLevel.Leased, ConsistencyLevel.Leased, true },
        { ConsistencyLevel.Leased, ConsistencyLevel.Strict, false },
        { ConsistencyLevel.Strict, ConsistencyLevel.Local, true },
        { ConsistencyLevel.Strict, ConsistencyLevel.BestEffort, true },
        { ConsistencyLevel.Strict, ConsistencyLevel.Leased, true },
        { ConsistencyLevel.Strict, ConsistencyLevel.Strict, true },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void SatisfiesFollowsTheFullTable(ConsistencyLevel declared, ConsistencyLevel required, bool expected)
    {
        Assert.Equal(expected, declared.Satisfies(required));
    }

    [Fact]
    public void TheTableCoversEveryPairOnce()
    {
        var levels = Enum.GetValues<ConsistencyLevel>();
        var pairs = Table.Select(row => (row.Data.Item1, row.Data.Item2)).ToList();

        Assert.Equal(levels.Length * levels.Length, pairs.Count);
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public void EachLevelHasItsSnakeCaseCode()
    {
        Assert.Equal(
            ["local", "best_effort", "leased", "strict"],
            Enum.GetValues<ConsistencyLevel>().Select(level => level.ToCode()));
    }

    [Fact]
    public void AnUndefinedLevelIsRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((ConsistencyLevel)9).Satisfies(ConsistencyLevel.Local)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ConsistencyLevel.Strict.Satisfies((ConsistencyLevel)9)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((ConsistencyLevel)9).ToCode()).Code);
    }

    [Fact]
    public void TheOtherEnumerationsHaveTheirCodes()
    {
        Assert.Equal(["atomic_operations", "leases", "fencing", "cache_invalidation"], CoordinationPrimitives.All.Select(primitive => primitive.ToCode()));
        Assert.Equal(["local", "redis", "postgresql"], Enum.GetValues<CoordinatorKind>().Select(kind => kind.ToCode()));
        Assert.Equal(["single_instance", "multi_replica"], Enum.GetValues<CoordinationTopology>().Select(topology => topology.ToCode()));
        Assert.Equal(["single_instance", "distributed"], Enum.GetValues<CoordinationMode>().Select(mode => mode.ToCode()));
        Assert.Equal(
            ["accepted", "unsupported_consistency", "coordinator_not_configured", "local_in_multi_replica", "degrade_not_permitted", "single_instance_violated"],
            Enum.GetValues<CapabilityReason>().Select(reason => reason.ToCode()));
        Assert.Equal(
            ["not_applicable", "held", "held_by_other", "lost", "unverified"],
            Enum.GetValues<SingleInstanceState>().Select(state => state.ToCode()));
    }
}

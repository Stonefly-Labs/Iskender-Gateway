using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using static Iskender.Domain.Tests.Ports.CapabilityFixtures;

namespace Iskender.Domain.Tests.Ports;

public class CapabilityCheckerTests
{
    /// <summary>What the coordinator for the checked primitive declares: a level, nothing, or no settings at all.</summary>
    public enum Declared
    {
        Local,
        BestEffort,
        Leased,
        Strict,
        NotOffered,
        NotConfigured,
    }

    private const CoordinationPrimitive Checked = CoordinationPrimitive.Leases;

    private const string Malformed = "malformed";

    private static readonly bool[] Flags = [false, true];

    /// <summary>
    /// Whether a distributed declaration (or none) meets a minimum, written out rather than derived: rows are
    /// <c>best_effort</c>, <c>leased</c>, <c>strict</c>, and not offered; columns are the minimums <c>local</c>,
    /// <c>best_effort</c>, <c>leased</c>, and <c>strict</c>.
    /// </summary>
    private static readonly Dictionary<Declared, bool[]> Meets = new()
    {
        [Declared.BestEffort] = [true, true, false, false],
        [Declared.Leased] = [true, true, true, false],
        [Declared.Strict] = [true, true, true, true],
        [Declared.NotOffered] = [false, false, false, false],
    };

    /// <summary>
    /// SC-002's requirement table: every topology × required level × declared level (with not offered and not
    /// configured) × outage behavior × mandatory flag × single-instance state, 960 rows, each with its expected result.
    /// </summary>
    public static TheoryData<CoordinationTopology, ConsistencyLevel, Declared, bool, bool, SingleInstanceState, string> Table()
    {
        var rows = new TheoryData<CoordinationTopology, ConsistencyLevel, Declared, bool, bool, SingleInstanceState, string>();
        foreach (var row in Rows())
        {
            rows.Add(row.Topology, row.Required, row.Declared, row.Degrades, row.Mandatory, row.State, row.Expected);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void EveryRowOfTheRequirementTableHasItsExpectedResult(
        CoordinationTopology topology, ConsistencyLevel required, Declared declared, bool degrades, bool mandatory, SingleInstanceState state, string expected)
    {
        var feature = Feature("fixture.row", Checked, required, mandatory, degrades ? OutageBehavior.Degrade("fixture.row_degraded") : OutageBehavior.FailClosed);

        if (expected == Malformed)
        {
            var failure = Assert.Throws<GatewayException>(() => Check([feature], topology, declared, state));
            Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
            Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
            return;
        }

        var report = Check([feature], topology, declared, state);
        var result = Assert.Single(report.Features);

        Assert.Equal(expected, Describe(result));
        Assert.Equal(topology, report.Topology);
        Assert.Equal(state, report.SingleInstance);
        Assert.Same(result, report.Find("fixture.row"));
        if (result.Accepted)
        {
            Assert.Equal(Level(declared), Assert.Single(result.Levels).Value);
        }
        else
        {
            Assert.Null(result.Mode);
            Assert.Empty(result.Levels);
        }
    }

    [Fact]
    public void TheTableCoversEveryReasonAndBothModes()
    {
        var outcomes = Rows().Select(row => row.Expected).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(960, Rows().Count());
        Assert.Contains(Malformed, outcomes);
        Assert.Contains("accepted:single_instance", outcomes);
        Assert.Contains("accepted:distributed", outcomes);
        foreach (var reason in Enum.GetValues<CapabilityReason>().Where(reason => reason != CapabilityReason.Accepted))
        {
            Assert.Contains("refused:" + reason.ToCode(), outcomes);
        }
    }

    [Fact]
    public void Scenario1MultiReplicaWithOnlyLocalSubstitutesRefusesALeasedLease()
    {
        var result = Single(LeasedLease, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Local), SingleInstanceState.NotApplicable);

        Assert.False(result.Accepted);
        Assert.Equal(CapabilityReason.LocalInMultiReplica, result.Reason);
    }

    [Fact]
    public void Scenario2NoTopologyGivesTheSameResultAsScenario1()
    {
        // An absent topology is read as multi_replica (contract Requirements rule 2, the section default in T010), and
        // T020 proves that end to end from a section with no topology field.
        var explicitly = Single(LeasedLease, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Local), SingleInstanceState.NotApplicable);
        var absent = CoordinationCapabilityChecker.Check([LeasedLease], CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Local), SingleInstanceState.NotApplicable);

        Assert.Equal(Describe(explicitly), Describe(Assert.Single(absent.Features)));
        Assert.Equal(CoordinationTopology.MultiReplica, absent.Topology);
        Assert.Equal("refused:local_in_multi_replica", Describe(explicitly));
    }

    [Fact]
    public void Scenario3SingleInstanceWithLocalSubstitutesAcceptsBestEffortAndLeasedAndRefusesStrict()
    {
        var report = CoordinationCapabilityChecker.Check([BestEffortCache, LeasedLease, StrictCounter], CoordinationTopology.SingleInstance, Uniform(CoordinatorCapabilities.Local), SingleInstanceState.Unverified);

        Assert.Equal("accepted:single_instance", Describe(report.Find(BestEffortCache.Name)!));
        Assert.Equal("accepted:single_instance", Describe(report.Find(LeasedLease.Name)!));
        Assert.Equal("refused:unsupported_consistency", Describe(report.Find(StrictCounter.Name)!));
        Assert.Equal(ConsistencyLevel.Local, report.Find(LeasedLease.Name)!.Levels[CoordinationPrimitive.Leases]);
        Assert.Equal(SingleInstanceState.Unverified, report.SingleInstance);
    }

    [Fact]
    public void Scenario4ACoordinatorDeclaringLeasedAtomicOperationsRefusesAStrictCounter()
    {
        var result = Single(StrictCounter, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Redis), SingleInstanceState.NotApplicable);

        Assert.Equal("refused:unsupported_consistency", Describe(result));
    }

    [Fact]
    public void Scenario5AHardBudgetPathThatDegradesIsRefused()
    {
        var result = Single(HardBudget, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable);

        Assert.Equal("refused:degrade_not_permitted", Describe(result));
    }

    [Fact]
    public void Scenario6ACoordinatorMeetingEveryRequirementAcceptsEveryFeatureAsDistributed()
    {
        var report = CoordinationCapabilityChecker.Check([StrictCounter, LeasedLease, BestEffortCache], CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable);

        Assert.All(report.Features, result =>
        {
            Assert.Equal("accepted:distributed", Describe(result));
            Assert.Equal(ConsistencyLevel.Strict, Assert.Single(result.Levels).Value);
        });
        Assert.Equal([StrictCounter.Name, LeasedLease.Name, BestEffortCache.Name], report.Features.Select(result => result.Feature.Name));
    }

    [Fact]
    public void ASecondSingleInstanceProcessIsRefusedWithSingleInstanceViolated()
    {
        var result = Single(LeasedLease, CoordinationTopology.SingleInstance, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.HeldByOther);

        Assert.Equal("refused:single_instance_violated", Describe(result));
    }

    [Fact]
    public void FencingAtLeasedIsRefusedOnRedisAndAcceptedOnPostgreSql()
    {
        var fencing = Feature("fixture.fenced_write", CoordinationPrimitive.Fencing, ConsistencyLevel.Leased);

        Assert.Equal("refused:unsupported_consistency", Describe(Single(fencing, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Redis), SingleInstanceState.NotApplicable)));
        Assert.Equal("accepted:distributed", Describe(Single(fencing, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable)));
    }

    [Fact]
    public void TheFirstMatchingRuleWinsAcrossPrimitives()
    {
        var both = new FeatureRequirement(
            "fixture.both",
            "fixture",
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased, [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict },
            mandatoryPath: false,
            OutageBehavior.FailClosed);
        var map = Uniform(CoordinatorCapabilities.Redis);

        // Rule 3 (Leases not configured) comes before rule 4 (AtomicOperations local) and rule 6.
        map[CoordinationPrimitive.AtomicOperations] = CoordinatorCapabilities.Local;
        map[CoordinationPrimitive.Leases] = null;
        Assert.Equal("refused:coordinator_not_configured", Describe(Single(both, CoordinationTopology.MultiReplica, map, SingleInstanceState.NotApplicable)));

        // Rule 4 comes before rule 6 (Redis leases below strict).
        map[CoordinationPrimitive.Leases] = CoordinatorCapabilities.Redis;
        Assert.Equal("refused:local_in_multi_replica", Describe(Single(both, CoordinationTopology.MultiReplica, map, SingleInstanceState.NotApplicable)));

        // In single_instance, a local primitive is fine, and the distributed one is still checked.
        Assert.Equal("refused:unsupported_consistency", Describe(Single(both, CoordinationTopology.SingleInstance, map, SingleInstanceState.Held)));

        // A primitive the feature does not use is never checked.
        map[CoordinationPrimitive.Leases] = CoordinatorCapabilities.PostgreSql;
        map[CoordinationPrimitive.CacheInvalidation] = null;
        var accepted = Single(both, CoordinationTopology.SingleInstance, map, SingleInstanceState.Held);
        Assert.Equal("accepted:single_instance", Describe(accepted));
        Assert.Equal(ConsistencyLevel.Local, accepted.Levels[CoordinationPrimitive.AtomicOperations]);
        Assert.Equal(ConsistencyLevel.Strict, accepted.Levels[CoordinationPrimitive.Leases]);

        // Rule 1 comes before rule 2.
        var degrading = Feature("fixture.degrading", Checked, ConsistencyLevel.Leased, mandatoryPath: true, OutageBehavior.Degrade("fixture.degraded"));
        Assert.Equal("refused:degrade_not_permitted", Describe(Single(degrading, CoordinationTopology.SingleInstance, map, SingleInstanceState.HeldByOther)));
    }

    [Fact]
    public void EachFeatureGetsItsOwnResult()
    {
        var report = CoordinationCapabilityChecker.Check(All, CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.Redis), SingleInstanceState.NotApplicable);

        Assert.Equal(
            ["refused:unsupported_consistency", "accepted:distributed", "accepted:distributed", "refused:degrade_not_permitted"],
            report.Features.Select(Describe));
        Assert.Null(report.Find("fixture.unknown"));
    }

    [Fact]
    public void ADuplicateFeatureNameIsMalformed()
    {
        var twin = Feature(LeasedLease.Name, CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Leased);

        var failure = Assert.Throws<GatewayException>(() => CoordinationCapabilityChecker.Check([LeasedLease, twin], CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);

        Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
    }

    [Fact]
    public void ALocalMinimumIsMalformedInMultiReplicaOnly()
    {
        var local = Feature("fixture.local_only", Checked, ConsistencyLevel.Local);

        var failure = Assert.Throws<GatewayException>(() => CoordinationCapabilityChecker.Check([local], CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable));
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));

        Assert.True(Single(local, CoordinationTopology.SingleInstance, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.Held).Accepted);
    }

    [Fact]
    public void AnEmptySetOfMinimumsIsMalformed()
    {
        var failure = Assert.Throws<GatewayException>(() => new FeatureRequirement("fixture.empty", "fixture", new Dictionary<CoordinationPrimitive, ConsistencyLevel>(), false, OutageBehavior.FailClosed));

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);

        Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
    }

    [Fact]
    public void InvalidArgumentsFailWithValueInvalid()
    {
        var map = Uniform(CoordinatorCapabilities.PostgreSql);
        var partial = new Dictionary<CoordinationPrimitive, CoordinatorCapabilities?>(map);
        partial.Remove(CoordinationPrimitive.CacheInvalidation);

        foreach (var call in new Action[]
        {
            () => CoordinationCapabilityChecker.Check(null!, CoordinationTopology.MultiReplica, map, SingleInstanceState.NotApplicable),
            () => CoordinationCapabilityChecker.Check([null!], CoordinationTopology.MultiReplica, map, SingleInstanceState.NotApplicable),
            () => CoordinationCapabilityChecker.Check([LeasedLease], CoordinationTopology.MultiReplica, null!, SingleInstanceState.NotApplicable),
            () => CoordinationCapabilityChecker.Check([LeasedLease], CoordinationTopology.MultiReplica, partial, SingleInstanceState.NotApplicable),
            () => CoordinationCapabilityChecker.Check([LeasedLease], (CoordinationTopology)99, map, SingleInstanceState.NotApplicable),
            () => CoordinationCapabilityChecker.Check([LeasedLease], CoordinationTopology.MultiReplica, map, (SingleInstanceState)99),
        })
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(call).Code);
        }
    }

    [Fact]
    public void NoFeaturesGiveAnEmptyReport()
    {
        var report = CoordinationCapabilityChecker.Check([], CoordinationTopology.SingleInstance, Uniform(CoordinatorCapabilities.Local), SingleInstanceState.Unverified);

        Assert.Empty(report.Features);
        Assert.Equal(SingleInstanceState.Unverified, report.SingleInstance);
    }

    private static IEnumerable<(CoordinationTopology Topology, ConsistencyLevel Required, Declared Declared, bool Degrades, bool Mandatory, SingleInstanceState State, string Expected)> Rows() =>
        from topology in Enum.GetValues<CoordinationTopology>()
        from required in Enum.GetValues<ConsistencyLevel>()
        from declared in Enum.GetValues<Declared>()
        from degrades in Flags
        from mandatory in Flags
        from state in Enum.GetValues<SingleInstanceState>()
        select (topology, required, declared, degrades, mandatory, state, Expect(topology, required, declared, degrades, mandatory, state));

    // The contract's rule table, row by row, with the level comparison taken from Meets.
    private static string Expect(CoordinationTopology topology, ConsistencyLevel required, Declared declared, bool degrades, bool mandatory, SingleInstanceState state)
    {
        var single = topology == CoordinationTopology.SingleInstance;
        var accepted = single ? "accepted:single_instance" : "accepted:distributed";
        return (required, declared) switch
        {
            (ConsistencyLevel.Local, _) when !single => Malformed,
            _ when mandatory && degrades => "refused:degrade_not_permitted",
            _ when single && state == SingleInstanceState.HeldByOther => "refused:single_instance_violated",
            (_, Declared.NotConfigured) => "refused:coordinator_not_configured",
            (_, Declared.Local) when !single => "refused:local_in_multi_replica",
            (ConsistencyLevel.Strict, Declared.Local) => "refused:unsupported_consistency",
            (_, Declared.Local) => "accepted:single_instance",
            _ => Meets[declared][(int)required] ? accepted : "refused:unsupported_consistency",
        };
    }

    private static CapabilityReport Check(IReadOnlyCollection<FeatureRequirement> features, CoordinationTopology topology, Declared declared, SingleInstanceState state)
    {
        var map = Uniform(CoordinatorCapabilities.PostgreSql);
        map[Checked] = declared switch
        {
            Declared.NotConfigured => null,
            Declared.NotOffered => new CoordinatorCapabilities(CoordinatorKind.Redis, new Dictionary<CoordinationPrimitive, ConsistencyLevel>()),
            Declared.Local => CoordinatorCapabilities.Local,
            _ => new CoordinatorCapabilities(CoordinatorKind.Redis, new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [Checked] = Level(declared) }),
        };
        return CoordinationCapabilityChecker.Check(features, topology, map, state);
    }

    private static ConsistencyLevel Level(Declared declared) => declared switch
    {
        Declared.Local => ConsistencyLevel.Local,
        Declared.BestEffort => ConsistencyLevel.BestEffort,
        Declared.Leased => ConsistencyLevel.Leased,
        Declared.Strict => ConsistencyLevel.Strict,
        _ => throw new ArgumentOutOfRangeException(nameof(declared)),
    };

    private static FeatureCapability Single(FeatureRequirement feature, CoordinationTopology topology, IReadOnlyDictionary<CoordinationPrimitive, CoordinatorCapabilities?> map, SingleInstanceState state) =>
        Assert.Single(CoordinationCapabilityChecker.Check([feature], topology, map, state).Features);

    private static string Describe(FeatureCapability result) =>
        result.Accepted ? "accepted:" + result.Mode!.Value.ToCode() : "refused:" + result.Reason.ToCode();
}

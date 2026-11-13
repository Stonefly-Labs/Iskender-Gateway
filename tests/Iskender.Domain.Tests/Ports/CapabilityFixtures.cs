using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

/// <summary>The fixture features from data-model "Test fixtures", and capability maps for the checker.</summary>
internal static class CapabilityFixtures
{
    public static FeatureRequirement StrictCounter { get; } = Feature("fixture.strict_counter", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict);

    public static FeatureRequirement LeasedLease { get; } = Feature("fixture.leased_lease", CoordinationPrimitive.Leases, ConsistencyLevel.Leased);

    public static FeatureRequirement BestEffortCache { get; } = Feature("fixture.best_effort_cache", CoordinationPrimitive.CacheInvalidation, ConsistencyLevel.BestEffort);

    /// <summary>A hard-budget path that declares <c>degrade</c>, which a mandatory path may not.</summary>
    public static FeatureRequirement HardBudget { get; } = new(
        "fixture.hard_budget",
        "fixture",
        new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict },
        mandatoryPath: true,
        OutageBehavior.Degrade("fixture.hard_budget_degraded"));

    public static IReadOnlyList<FeatureRequirement> All { get; } = [StrictCounter, LeasedLease, BestEffortCache, HardBudget];

    public static FeatureRequirement Feature(string name, CoordinationPrimitive primitive, ConsistencyLevel minimum, bool mandatoryPath = false, OutageBehavior? outage = null) =>
        new(name, "fixture", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum }, mandatoryPath, outage ?? OutageBehavior.FailClosed);

    /// <summary>The same coordinator, or <c>null</c> for one that is not configured, for every primitive.</summary>
    public static Dictionary<CoordinationPrimitive, CoordinatorCapabilities?> Uniform(CoordinatorCapabilities? capabilities) =>
        Enum.GetValues<CoordinationPrimitive>().ToDictionary(primitive => primitive, _ => capabilities);
}

using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Diagnostics;

public class CapabilityReportLogTests
{
    [Fact]
    public void EachAcceptedFeatureWrites5001WithItsModeAndLevelsAndEachRefusedOne5002WithItsReason()
    {
        var logger = new CapturingDiagnostics.Logger();
        var report = CoordinationCapabilityChecker.Check(
            [Feature("report_a", "fixture.counter", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Leased), Feature("report_a", "fixture.strict", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict)],
            CoordinationTopology.MultiReplica,
            Uniform(CoordinatorCapabilities.Redis),
            SingleInstanceState.NotApplicable);

        CoordinationLog.CapabilityReport(logger, report);

        Assert.Equal([5001, 5002], logger.Events.Select(logged => logged.Id.Id));
        var accepted = Fields(logger.Events[0]);
        Assert.Equal("fixture.counter", accepted["Feature"]);
        Assert.Equal("report_a", accepted["Module"]);
        Assert.Equal("distributed", accepted["Outcome"]);
        Assert.Equal("atomic_operations=leased", accepted["ConsistencyLevel"]);
        var refused = Fields(logger.Events[1]);
        Assert.Equal("fixture.strict", refused["Feature"]);
        Assert.Equal("unsupported_consistency", refused["Reason"]);
    }

    [Fact]
    public void EachRefusalIsCountedOnceWithItsModuleAndReason()
    {
        var module = "report_" + Guid.NewGuid().ToString("N")[..8];
        var report = CoordinationCapabilityChecker.Check(
            [
                Feature(module, "fixture.one", CoordinationPrimitive.Leases, ConsistencyLevel.Leased),
                Feature(module, "fixture.two", CoordinationPrimitive.CacheInvalidation, ConsistencyLevel.BestEffort),
                Feature(module, "fixture.three", CoordinationPrimitive.Leases, ConsistencyLevel.Strict, mandatoryPath: true, OutageBehavior.Degrade("fixture.three_degraded")),
            ],
            CoordinationTopology.MultiReplica,
            Uniform(CoordinatorCapabilities.Local),
            SingleInstanceState.NotApplicable);

        CoordinationLog.CapabilityReport(new CapturingDiagnostics.Logger(), report);

        var counted = CapturingDiagnostics.Shared.Measurements
            .Where(measurement => measurement.Instrument == "iskender.coordination.capability.refused" && measurement.Labels.Any(label => label.Key == CoordinationTelemetry.ModuleTag && Equals(label.Value, module)))
            .Select(measurement => (measurement.Value, Reason: measurement.Labels.Single(label => label.Key == CoordinationTelemetry.OutcomeTag).Value as string))
            .OrderBy(entry => entry.Reason, StringComparer.Ordinal)
            .ToList();
        Assert.Equal([(1d, "degrade_not_permitted"), (1d, "local_in_multi_replica"), (1d, "local_in_multi_replica")], counted);
    }

    [Fact]
    public void TheCheckIsRecordedAsOneCapabilityCheckActivity()
    {
        var report = CoordinationCapabilityChecker.Check(
            [Feature("report_c", "fixture.activity", CoordinationPrimitive.Leases, ConsistencyLevel.Leased)],
            CoordinationTopology.SingleInstance,
            Uniform(CoordinatorCapabilities.Local),
            SingleInstanceState.Unverified);

        CoordinationLog.CapabilityReport(new CapturingDiagnostics.Logger(), report);

        Assert.Contains(CapturingDiagnostics.Shared.Activities, activity => activity.Name == "coordination.capability_check"
            && activity.Tags.Any(tag => tag.Key == CoordinationTelemetry.OutcomeTag && Equals(tag.Value, "accepted")));
        Assert.All(CapturingDiagnostics.Shared.Activities.SelectMany(activity => activity.Tags), tag => Assert.Contains(tag.Key, CoordinationTelemetry.AllowedTags));
    }

    [Fact]
    public void ALocalSubstituteIsNeverReportedAsDistributedInAnyEvent()
    {
        var logger = new CapturingDiagnostics.Logger();
        var levels = new[] { ConsistencyLevel.BestEffort, ConsistencyLevel.Leased, ConsistencyLevel.Strict };
        foreach (var topology in Enum.GetValues<CoordinationTopology>())
        {
            foreach (var local in Enum.GetValues<CoordinationPrimitive>())
            {
                // One primitive on the local substitute, the rest on PostgreSQL, and a feature per primitive and level.
                var map = Uniform(CoordinatorCapabilities.PostgreSql);
                map[local] = CoordinatorCapabilities.Local;
                var features = Enum.GetValues<CoordinationPrimitive>()
                    .SelectMany(primitive => levels.Select(level => Feature("report_d", $"fixture.{primitive.ToCode()}_{level.ToCode()}", primitive, level)))
                    .ToList();
                var state = topology == CoordinationTopology.SingleInstance ? SingleInstanceState.Unverified : SingleInstanceState.NotApplicable;

                CoordinationLog.CapabilityReport(logger, CoordinationCapabilityChecker.Check(features, topology, map, state));
            }
        }

        var localFeatures = logger.Events.Where(logged => logged.Id.Id == 5001 && Fields(logged)["ConsistencyLevel"] is string levelsText && levelsText.Contains("=local", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(localFeatures);
        Assert.All(localFeatures, logged => Assert.Equal("single_instance", Fields(logged)["Outcome"]));

        // In multi_replica, a feature on the local substitute is refused rather than reported at all as accepted.
        Assert.Contains(logger.Events, logged => logged.Id.Id == 5002 && Equals(Fields(logged)["Reason"], "local_in_multi_replica"));
    }

    private static FeatureRequirement Feature(string module, string name, CoordinationPrimitive primitive, ConsistencyLevel minimum, bool mandatoryPath = false, OutageBehavior? outage = null) =>
        new(name, module, new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum }, mandatoryPath, outage ?? OutageBehavior.FailClosed);

    private static Dictionary<CoordinationPrimitive, CoordinatorCapabilities?> Uniform(CoordinatorCapabilities capabilities) =>
        Enum.GetValues<CoordinationPrimitive>().ToDictionary(primitive => primitive, _ => (CoordinatorCapabilities?)capabilities);

    private static Dictionary<string, object?> Fields(CapturedLog logged) => logged.Fields.ToDictionary(field => field.Key, field => field.Value);
}

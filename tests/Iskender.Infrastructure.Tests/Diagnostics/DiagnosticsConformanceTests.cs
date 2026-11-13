using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Security;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Tests.Diagnostics;

/// <summary>
/// The coordination diagnostics against the contract's Diagnostics section and research R18 (FR-028). It drives one
/// operation of each kind through every implementation that needs no container (the local substitutes, the checker and
/// gate, the health probe, and the single-instance guard) and writes each event once, then checks everything captured in
/// this run: every event has its contract ID, name, and level, every field, tag, and label name is on the contract's list,
/// and every activity and instrument name is one research R18 names.
/// </summary>
public class DiagnosticsConformanceTests
{
    // The contract's Diagnostics table.
    private static readonly Dictionary<int, (string Name, LogLevel Level)> Events = new()
    {
        [5001] = ("CoordinationFeatureAccepted", LogLevel.Information),
        [5002] = ("CoordinationFeatureRefused", LogLevel.Warning),
        [5003] = ("CoordinationConnectionFailed", LogLevel.Warning),
        [5004] = ("CoordinationOutcomeUnknown", LogLevel.Error),
        [5005] = ("CoordinationLeaseLost", LogLevel.Warning),
        [5006] = ("CoordinationFencingRejected", LogLevel.Warning),
        [5007] = ("CoordinationCacheInvalidated", LogLevel.Debug),
        [5008] = ("CoordinationCacheDegraded", LogLevel.Warning),
        [5009] = ("CoordinationHealthChanged", LogLevel.Information),
        [5010] = ("CoordinationUnexpectedFailure", LogLevel.Error),
        [5011] = ("CoordinationSingleInstanceChanged", LogLevel.Warning),
    };

    // Feature, module, primitive, operation, outcome, failure class, consistency level, coordinator kind, and reason code.
    private static readonly HashSet<string> FieldNames = new(StringComparer.Ordinal)
    {
        "Feature", "Module", "Primitive", "Operation", "Outcome", "FailureClass", "ConsistencyLevel", "CoordinatorKind", "Reason",
    };

    private static readonly HashSet<string> TagNames = new(StringComparer.Ordinal)
    {
        "iskender.feature", "iskender.module", "iskender.primitive", "iskender.operation", "iskender.outcome", "iskender.failure_class",
        "iskender.consistency_level", "iskender.coordinator_kind", "iskender.reason",
    };

    private static readonly HashSet<string> ActivityNames = new(StringComparer.Ordinal) { "coordination.operation", "coordination.capability_check" };

    private static readonly HashSet<string> InstrumentNames = new(StringComparer.Ordinal)
    {
        "iskender.coordination.operations", "iskender.coordination.operation.duration", "iskender.coordination.lease.lost",
        "iskender.coordination.fencing.rejected", "iskender.coordination.capability.refused",
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryCapturedDiagnosticMatchesTheContract()
    {
        var logger = await DriveAsync();

        Assert.Equal(Events.Keys.Order(), logger.Events.Select(logged => logged.Id.Id).Distinct().Order());
        Assert.All(CapturingDiagnostics.Shared.Logs.Where(logged => logged.Id.Id is >= 5000 and < 6000), logged =>
        {
            Assert.True(Events.TryGetValue(logged.Id.Id, out var expected), $"Event {logged.Id.Id} is not in the contract.");
            Assert.Equal(expected.Name, logged.Id.Name);
            Assert.Equal(expected.Level, logged.Level);
            Assert.All(logged.Fields, field => Assert.Contains(field.Key, FieldNames));
        });
    }

    [Fact]
    public async Task EveryActivityAndInstrumentHasAContractNameAndOnlyContractTagsAndLabels()
    {
        await DriveAsync();

        Assert.Contains(CapturingDiagnostics.Shared.Activities, activity => activity.Name == "coordination.operation");
        Assert.Contains(CapturingDiagnostics.Shared.Activities, activity => activity.Name == "coordination.capability_check");
        Assert.All(CapturingDiagnostics.Shared.Activities, activity =>
        {
            Assert.Contains(activity.Name, ActivityNames);
            Assert.All(activity.Tags, tag => Assert.Contains(tag.Key, TagNames));
        });

        Assert.Equal(InstrumentNames.Order(StringComparer.Ordinal), CapturingDiagnostics.Shared.Measurements.Select(measurement => measurement.Instrument).Distinct().Order(StringComparer.Ordinal));
        Assert.All(CapturingDiagnostics.Shared.Measurements, measurement =>
        {
            Assert.Contains(measurement.Instrument, InstrumentNames);
            Assert.All(measurement.Labels, label => Assert.Contains(label.Key, TagNames));
        });
    }

    // One operation of each kind on the local substitutes, then each event the substitutes cannot raise, written once
    // through its helper, so that every definition is checked.
    private static async Task<CapturingDiagnostics.Logger> DriveAsync()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var logger = coordinator.Logger;
        var client = coordinator.CreateClient(0);
        var scope = coordinator.Scopes.OrganizationA;
        var duration = coordinator.Settings.MinimumLeaseDuration;
        await CanaryDriver.DriveAsync(client, scope, duration, Token);

        var lease = (await client.Leases.AcquireAsync(scope, new CoordinationName("diagnostics", "lost"), duration, LeaseWait.TryOnly, Token)).Lease!;
        await coordinator.PassTimeAsync(duration + TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<GatewayException>(async () => await client.Leases.RenewAsync(lease, duration, Token));

        var accepted = new FeatureRequirement("diagnostics.counter", "diagnostics", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.BestEffort }, mandatoryPath: false, OutageBehavior.FailClosed);
        var refused = new FeatureRequirement("diagnostics.strict_counter", "diagnostics", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict }, mandatoryPath: false, OutageBehavior.FailClosed);
        var report = CoordinationCapabilityChecker.Check([accepted, refused], CoordinationTopology.SingleInstance, CoordinationPrimitives.All.ToDictionary(primitive => primitive, _ => (CoordinatorCapabilities?)CoordinatorCapabilities.Local), SingleInstanceState.Unverified);
        CoordinationLog.CapabilityReport(logger, report);

        var settings = LocalCoordinatorUnderTest.ReadSettings("\"atomic\":{\"coordinator\":\"redis\"},\"redis\":{\"endpoints\":[\"cache.internal:6379\"]}");
        await new CoordinationHealthProbe(settings, postgreSqlConfigured: false, new Failing(), null, logger).CheckAsync(Token);

        CoordinationLog.LogConnectionFailed(logger, CoordinatorKind.Redis, CoordinationFailureClass.Unavailable);
        CoordinationLog.LogOutcomeUnknown(logger, "diagnostics", CoordinationPrimitive.AtomicOperations, "atomic.reserve", CoordinatorKind.Redis);
        CoordinationLog.LogFencingRejected(logger, "diagnostics", "stale_token");
        CoordinationTelemetry.RecordFencingRejected("diagnostics");
        CoordinationLog.LogCacheDegraded(logger, "diagnostics", CoordinatorKind.Redis, CoordinationFailureClass.Timeout);
        CoordinationLog.LogSingleInstanceChanged(logger, SingleInstanceState.Lost, SingleInstanceState.Held);
        return logger;
    }

    private sealed class Failing : ICoordinationHealthProbe
    {
        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("unclassified");
    }
}

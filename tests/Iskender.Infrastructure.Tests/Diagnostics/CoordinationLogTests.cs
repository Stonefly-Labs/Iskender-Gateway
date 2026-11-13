using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Tests.Diagnostics;

public class CoordinationLogTests
{
    /// <summary>The contract's allowed fields: feature, module, primitive, operation, outcome, failure class, consistency level, coordinator kind, reason code.</summary>
    internal static readonly HashSet<string> AllowedFields = ["Feature", "Module", "Primitive", "Operation", "Outcome", "FailureClass", "ConsistencyLevel", "CoordinatorKind", "Reason"];

    private static readonly FeatureRequirement Counter = new(
        "fixture.strict_counter",
        "fixture",
        new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict, [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased },
        mandatoryPath: false,
        OutageBehavior.FailClosed);

    public static TheoryData<int, string, LogLevel> Events() => new()
    {
        { 5001, "CoordinationFeatureAccepted", LogLevel.Information },
        { 5002, "CoordinationFeatureRefused", LogLevel.Warning },
        { 5003, "CoordinationConnectionFailed", LogLevel.Warning },
        { 5004, "CoordinationOutcomeUnknown", LogLevel.Error },
        { 5005, "CoordinationLeaseLost", LogLevel.Warning },
        { 5006, "CoordinationFencingRejected", LogLevel.Warning },
        { 5007, "CoordinationCacheInvalidated", LogLevel.Debug },
        { 5008, "CoordinationCacheDegraded", LogLevel.Warning },
        { 5009, "CoordinationHealthChanged", LogLevel.Information },
        { 5010, "CoordinationUnexpectedFailure", LogLevel.Error },
        { 5011, "CoordinationSingleInstanceChanged", LogLevel.Warning },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void EachEventHasItsIdNameLevelAndOnlyAllowedFields(int id, string name, LogLevel level)
    {
        var logger = new CapturingDiagnostics.Logger();

        WriteEvery(logger);

        var logged = Assert.Single(logger.Events, logged => logged.Id.Id == id);
        Assert.Equal(name, logged.Id.Name);
        Assert.Equal(level, logged.Level);
        Assert.NotEmpty(logged.Fields);
        Assert.All(logged.Fields, field => Assert.Contains(field.Key, AllowedFields));
        Assert.Null(logged.Exception);
    }

    [Fact]
    public void ElevenEventsAreDefinedAndNoneRepeats()
    {
        var logger = new CapturingDiagnostics.Logger();

        WriteEvery(logger);

        Assert.Equal(Enumerable.Range(5001, 11), logger.Events.Select(logged => logged.Id.Id));
    }

    [Fact]
    public void AnAcceptedFeatureIsWrittenWithItsModeAndLevelsInPrimitiveOrder()
    {
        var logger = new CapturingDiagnostics.Logger();
        var levels = new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict, [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict };

        CoordinationLog.LogFeatureAccepted(logger, FeatureCapability.Accept(Counter, CoordinationMode.Distributed, levels));

        var fields = Assert.Single(logger.Events).Fields.ToDictionary(field => field.Key, field => field.Value);
        Assert.Equal("fixture.strict_counter", fields["Feature"]);
        Assert.Equal("distributed", fields["Outcome"]);
        Assert.Equal("atomic_operations=strict,leases=strict", fields["ConsistencyLevel"]);
    }

    [Fact]
    public void ARefusedFeatureIsWrittenWithItsReasonCode()
    {
        var logger = new CapturingDiagnostics.Logger();

        CoordinationLog.LogFeatureRefused(logger, FeatureCapability.Refuse(Counter, CapabilityReason.LocalInMultiReplica));

        Assert.Equal("local_in_multi_replica", Assert.Single(logger.Events).Fields.Single(field => field.Key == "Reason").Value);
    }

    [Fact]
    public void EveryActivityTagAndMetricLabelIsAnAllowedName()
    {
        var diagnostics = CapturingDiagnostics.Shared;

        WriteEveryInstrument();

        Assert.Contains(diagnostics.Activities, activity => activity.Name == "coordination.operation");
        Assert.Contains(diagnostics.Activities, activity => activity.Name == "coordination.capability_check");
        Assert.Equal(
            ["iskender.coordination.capability.refused", "iskender.coordination.fencing.rejected", "iskender.coordination.lease.lost", "iskender.coordination.operation.duration", "iskender.coordination.operations"],
            diagnostics.Measurements.Select(measurement => measurement.Instrument).Distinct().Order(StringComparer.Ordinal));
        Assert.All(diagnostics.Activities.SelectMany(activity => activity.Tags), tag => Assert.Contains(tag.Key, CoordinationTelemetry.AllowedTags));
        Assert.All(diagnostics.Measurements.SelectMany(measurement => measurement.Labels), label => Assert.Contains(label.Key, CoordinationTelemetry.AllowedTags));
    }

    [Fact]
    public void TheAllowedTagsAreTheContractsSevenLabels()
    {
        Assert.Equal(
            ["iskender.module", "iskender.primitive", "iskender.operation", "iskender.outcome", "iskender.failure_class", "iskender.consistency_level", "iskender.coordinator_kind"],
            CoordinationTelemetry.AllowedTags);
        Assert.Equal("Iskender.Coordination", CoordinationTelemetry.SourceName);
        Assert.Equal(CapturingDiagnostics.SourceName, CoordinationTelemetry.Source.Name);
    }

    private static void WriteEvery(ILogger logger)
    {
        var levels = new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict, [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased };
        CoordinationLog.LogFeatureAccepted(logger, FeatureCapability.Accept(Counter, CoordinationMode.Distributed, levels));
        CoordinationLog.LogFeatureRefused(logger, FeatureCapability.Refuse(Counter, CapabilityReason.CoordinatorNotConfigured));
        CoordinationLog.LogConnectionFailed(logger, CoordinatorKind.Redis, CoordinationFailureClass.Unavailable);
        CoordinationLog.LogOutcomeUnknown(logger, "fixture", CoordinationPrimitive.AtomicOperations, "add", CoordinatorKind.Redis);
        CoordinationLog.LogLeaseLost(logger, "fixture", CoordinatorKind.PostgreSql);
        CoordinationLog.LogFencingRejected(logger, "fixture", "stale_token");
        CoordinationLog.LogCacheInvalidated(logger, "fixture", CoordinatorKind.Local);
        CoordinationLog.LogCacheDegraded(logger, "fixture", CoordinatorKind.Redis, CoordinationFailureClass.Timeout);
        CoordinationLog.LogHealthChanged(logger, CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly));
        CoordinationLog.LogUnexpectedFailure(logger, "fixture", "reserve", CoordinatorKind.Redis, CoordinationFailureClass.Unexpected);
        CoordinationLog.LogSingleInstanceChanged(logger, SingleInstanceState.Lost, SingleInstanceState.Held);
    }

    private static void WriteEveryInstrument()
    {
        CoordinationTelemetry.End(CoordinationTelemetry.StartOperation("fixture", CoordinationPrimitive.Leases, "acquire", CoordinatorKind.PostgreSql), "failed", CoordinationFailureClass.Timeout);
        CoordinationTelemetry.End(CoordinationTelemetry.StartCapabilityCheck(), "completed");
        CoordinationTelemetry.RecordOperation("fixture", CoordinationPrimitive.AtomicOperations, "add", CoordinatorKind.Redis, "failed", TimeSpan.FromMilliseconds(3), CoordinationFailureClass.Unavailable);
        CoordinationTelemetry.RecordLeaseLost("fixture", CoordinatorKind.Redis);
        CoordinationTelemetry.RecordFencingRejected("fixture");
        CoordinationTelemetry.RecordCapabilityRefused(FeatureCapability.Refuse(Counter, CapabilityReason.UnsupportedConsistency));
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The coordination activities and metrics, from <c>ActivitySource</c> and <c>Meter</c> named
/// <c>Iskender.Coordination</c> (FR-028, research R18). Tags and labels are only module, primitive, operation, outcome,
/// failure class, consistency level, and coordinator kind. The Redis client's own logging is never connected. Public
/// because the Redis and PostgreSQL adapters record these too. Owned by ISK-0005.
/// </summary>
public static class CoordinationTelemetry
{
    public const string SourceName = "Iskender.Coordination";
    public const string ModuleTag = "iskender.module";
    public const string PrimitiveTag = "iskender.primitive";
    public const string OperationTag = "iskender.operation";
    public const string OutcomeTag = "iskender.outcome";
    public const string FailureClassTag = "iskender.failure_class";
    public const string ConsistencyLevelTag = "iskender.consistency_level";
    public const string CoordinatorKindTag = "iskender.coordinator_kind";

    /// <summary>Every tag and label name this class writes.</summary>
    public static IReadOnlyList<string> AllowedTags { get; } =
        [ModuleTag, PrimitiveTag, OperationTag, OutcomeTag, FailureClassTag, ConsistencyLevelTag, CoordinatorKindTag];

    public static readonly ActivitySource Source = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    private static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "iskender.coordination.operations", "{operation}", "Coordination operations, by outcome.");

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "iskender.coordination.operation.duration", "s", "Time from starting a coordination operation to its result.");

    private static readonly Counter<long> LeasesLost = Meter.CreateCounter<long>(
        "iskender.coordination.lease.lost", "{lease}", "Leases found lost on renewal.");

    private static readonly Counter<long> FencingRejections = Meter.CreateCounter<long>(
        "iskender.coordination.fencing.rejected", "{rejection}", "Writes the fencing guard rejected.");

    private static readonly Counter<long> CapabilityRefusals = Meter.CreateCounter<long>(
        "iskender.coordination.capability.refused", "{feature}", "Features the capability check refused.");

    public static Activity? StartOperation(string module, CoordinationPrimitive primitive, string operation, CoordinatorKind coordinator) =>
        Source.StartActivity("coordination.operation", ActivityKind.Client, parentContext: default,
            [new(ModuleTag, module), new(PrimitiveTag, primitive.ToCode()), new(OperationTag, operation), new(CoordinatorKindTag, coordinator.ToCode())]);

    public static Activity? StartCapabilityCheck() =>
        Source.StartActivity("coordination.capability_check", ActivityKind.Internal);

    /// <summary>Sets the outcome tag, and the failure class tag on a failure, then stops <paramref name="activity"/>.</summary>
    public static void End(Activity? activity, string outcome, CoordinationFailureClass? failureClass = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OutcomeTag, outcome);
        if (failureClass is { } value)
        {
            activity.SetTag(FailureClassTag, value.ToCode());
            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.Dispose();
    }

    /// <summary>Counts one operation and records its duration, with the failure class on a failure.</summary>
    public static void RecordOperation(
        string module,
        CoordinationPrimitive primitive,
        string operation,
        CoordinatorKind coordinator,
        string outcome,
        TimeSpan duration,
        CoordinationFailureClass? failureClass = null)
    {
        var labels = new TagList
        {
            { ModuleTag, module },
            { PrimitiveTag, primitive.ToCode() },
            { OperationTag, operation },
            { CoordinatorKindTag, coordinator.ToCode() },
            { OutcomeTag, outcome },
        };
        if (failureClass is { } value)
        {
            labels.Add(FailureClassTag, value.ToCode());
        }

        Operations.Add(1, labels);
        OperationDuration.Record(duration.TotalSeconds, labels);
    }

    /// <summary>
    /// Runs <paramref name="work"/> under one operation activity and records its count and duration, with the failure
    /// class when it fails with a coordination failure. The failure is rethrown unchanged.
    /// </summary>
    public static async ValueTask<T> MeasureAsync<T>(
        string module,
        CoordinationPrimitive primitive,
        string operation,
        CoordinatorKind coordinator,
        TimeProvider timeProvider,
        Func<ValueTask<T>> work)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(work);
        var started = timeProvider.GetTimestamp();
        var activity = StartOperation(module, primitive, operation, coordinator);
        try
        {
            var result = await work();
            End(activity, "succeeded");
            RecordOperation(module, primitive, operation, coordinator, "succeeded", timeProvider.GetElapsedTime(started));
            return result;
        }
        catch (GatewayException failure)
        {
            var failureClass = CoordinationFailures.ClassOf(failure);
            End(activity, "failed", failureClass);
            RecordOperation(module, primitive, operation, coordinator, "failed", timeProvider.GetElapsedTime(started), failureClass);
            throw;
        }
    }

    public static void RecordLeaseLost(string module, CoordinatorKind coordinator) =>
        LeasesLost.Add(1, new(ModuleTag, module), new(CoordinatorKindTag, coordinator.ToCode()));

    public static void RecordFencingRejected(string module) =>
        FencingRejections.Add(1, new KeyValuePair<string, object?>(ModuleTag, module));

    /// <summary>Counts one refusal, labeled with the feature's module and the refusal reason as the outcome.</summary>
    public static void RecordCapabilityRefused(FeatureCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        CapabilityRefusals.Add(1, new(ModuleTag, capability.Feature.Module), new(OutcomeTag, capability.Reason.ToCode()));
    }
}

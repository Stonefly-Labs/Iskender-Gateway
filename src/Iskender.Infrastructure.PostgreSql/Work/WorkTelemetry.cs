using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The work activities and metrics, from <c>ActivitySource</c> and <c>Meter</c> named <c>Iskender.Work</c> (<c>work</c>
/// contract Tracing and Diagnostics rule 3, FR-033). Tags and labels are only kind, job, outcome, failure class, error
/// code, and attempt. Owned by ISK-0009.
/// </summary>
internal static class WorkTelemetry
{
    public const string SourceName = "Iskender.Work";
    public const string KindTag = "iskender.work.kind";
    public const string AttemptTag = "iskender.work.attempt";
    public const string OutcomeTag = "iskender.work.outcome";
    public const string JobTag = "iskender.work.job";
    public const string KindLabel = "kind";
    public const string OutcomeLabel = "outcome";
    public const string FailureClassLabel = "failure_class";
    public const string ErrorCodeLabel = "error_code";
    public const string JobLabel = "job";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Every activity tag name this class writes.</summary>
    public static IReadOnlyList<string> AllowedTags { get; } = ImmutableArray.Create(KindTag, AttemptTag, OutcomeTag, JobTag);

    /// <summary>Every metric label name this class writes.</summary>
    public static IReadOnlyList<string> AllowedLabels { get; } = ImmutableArray.Create(KindLabel, OutcomeLabel, FailureClassLabel, ErrorCodeLabel, JobLabel);

    private static readonly Meter Meter = new(SourceName);

    private static readonly ConcurrentDictionary<string, long> Skewed = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, long> Backlog = new(StringComparer.Ordinal);

    private static readonly Counter<long> Enqueued = Meter.CreateCounter<long>(
        "iskender.work.items.enqueued", "{item}", "Work items enqueued, by outcome.");

    private static readonly Counter<long> Dispatched = Meter.CreateCounter<long>(
        "iskender.work.items.dispatched", "{item}", "Work item attempts started.");

    private static readonly Counter<long> Completed = Meter.CreateCounter<long>(
        "iskender.work.items.completed", "{item}", "Work items completed.");

    private static readonly Counter<long> Retried = Meter.CreateCounter<long>(
        "iskender.work.items.retried", "{item}", "Work item attempts that failed and were scheduled again.");

    private static readonly Counter<long> Poisoned = Meter.CreateCounter<long>(
        "iskender.work.items.poisoned", "{item}", "Work items poisoned.");

    private static readonly ObservableGauge<long> SkewedGauge = Meter.CreateObservableGauge(
        "iskender.work.items.skewed", () => Observe(Skewed), "{item}", "Due pending items no handler in this worker can take, as of its last pass.");

    private static readonly ObservableGauge<long> BacklogGauge = Meter.CreateObservableGauge(
        "iskender.work.backlog", () => Observe(Backlog), "{item}", "Due pending items this worker can handle, as of its last pass.");

    private static readonly Histogram<double> DispatchDuration = Meter.CreateHistogram<double>(
        "iskender.work.dispatch.duration", "s", "Time from claiming a work item to its outcome.");

    private static readonly Counter<long> JobRuns = Meter.CreateCounter<long>(
        "iskender.work.job.runs", "{run}", "Recurring job runs, by outcome.");

    /// <summary>
    /// Starts <c>work.dispatch</c>, of kind <c>Consumer</c>, with no parent and one link to <paramref name="recorded"/>
    /// when it is set (Tracing rule 2). A default parent context still parents under <see cref="Activity.Current"/>, so the
    /// ambient activity is cleared first; it stays cleared on the calling flow, the dispatcher's own loop.
    /// </summary>
    public static Activity? StartDispatch(WorkKind kind, int attempt, ActivityContext? recorded)
    {
        var ambient = Activity.Current;
        Activity.Current = null;
        var activity = Source.StartActivity(
            "work.dispatch",
            ActivityKind.Consumer,
            parentContext: default,
            [new(KindTag, kind.ToString()), new(AttemptTag, attempt)],
            recorded is { } context ? [new ActivityLink(context)] : null);
        if (activity is null)
        {
            Activity.Current = ambient;
        }

        return activity;
    }

    /// <summary>Starts <c>work.job</c> (Tracing rule 3).</summary>
    public static Activity? StartJob(string job) =>
        Source.StartActivity("work.job", ActivityKind.Internal, parentContext: default, [new(JobTag, job)]);

    /// <summary>Sets the outcome tag, then stops <paramref name="activity"/>.</summary>
    public static void End(Activity? activity, string outcome)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OutcomeTag, outcome);
        activity.Stop();
    }

    public static void RecordEnqueued(WorkKind kind, bool created) =>
        Enqueued.Add(1, new(KindLabel, kind.ToString()), new(OutcomeLabel, created ? WorkOutcomes.Created : WorkOutcomes.Existing));

    public static void RecordDispatched(WorkKind kind) => Dispatched.Add(1, new KeyValuePair<string, object?>(KindLabel, kind.ToString()));

    public static void RecordCompleted(WorkKind kind) => Completed.Add(1, new KeyValuePair<string, object?>(KindLabel, kind.ToString()));

    public static void RecordRetried(WorkKind kind, WorkFailure failure) =>
        Retried.Add(1, new(KindLabel, kind.ToString()), new(FailureClassLabel, failure.Class.ToCode()), new(ErrorCodeLabel, failure.Code.Id));

    public static void RecordPoisoned(WorkKind kind, WorkFailure failure) =>
        Poisoned.Add(1, new(KindLabel, kind.ToString()), new(FailureClassLabel, failure.Class.ToCode()), new(ErrorCodeLabel, failure.Code.Id));

    public static void RecordDispatchDuration(WorkKind kind, string outcome, TimeSpan elapsed) =>
        DispatchDuration.Record(elapsed.TotalSeconds, new(KindLabel, kind.ToString()), new(OutcomeLabel, outcome));

    public static void RecordJobRun(string job, string outcome) =>
        JobRuns.Add(1, new(JobLabel, job), new(OutcomeLabel, WorkOutcomes.RequireJob(outcome)));

    /// <summary>Sets the skewed gauge for <paramref name="kind"/>, as of the worker's last pass.</summary>
    public static void SetSkewed(WorkKind kind, long count) => Skewed[kind.ToString()] = count;

    /// <summary>Sets the backlog gauge for <paramref name="kind"/>, as of the worker's last pass.</summary>
    public static void SetBacklog(WorkKind kind, long count) => Backlog[kind.ToString()] = count;

    private static IEnumerable<Measurement<long>> Observe(ConcurrentDictionary<string, long> values) =>
        values.Select(value => new Measurement<long>(value.Value, new KeyValuePair<string, object?>(KindLabel, value.Key)));
}

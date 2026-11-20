using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Microsoft.Extensions.Logging;

namespace Iskender.Work.Tests.Telemetry;

/// <summary>
/// The <c>work</c> contract's events, activities, and instruments, pinned exactly (ISK-0009 T017, Diagnostics, Tracing,
/// FR-033): every event's ID, name, level, and template, with only the allowed fields; every instrument's name, kind,
/// and label set.
/// </summary>
public class WorkLogTests
{
    /// <summary>The contract's allowed fields: module, kind, job, outcome, failure class, error code, and attempt.</summary>
    private static readonly HashSet<string> AllowedFields = ["Module", "Kind", "Job", "Outcome", "FailureClass", "Code", "Attempt"];

    private static readonly WorkKind Kind = new("routing", "refresh");

    private static readonly WorkFailure Failure = new(WorkFailureClass.Transient, ErrorCodes.PersistenceTimeout);

    public static TheoryData<int, string, LogLevel, string> Events() => new()
    {
        { 8001, "WorkDispatcherStarted", LogLevel.Information, "Iskender work dispatcher started." },
        { 8002, "WorkDispatcherStopping", LogLevel.Information, "Iskender work dispatcher stopping." },
        { 8003, "WorkDispatcherStopped", LogLevel.Information, "Iskender work dispatcher stopped: outcome {Outcome}." },
        { 8004, "WorkItemEnqueued", LogLevel.Debug, "Iskender work item enqueued: kind {Kind}, outcome {Outcome}." },
        { 8005, "WorkItemCompleted", LogLevel.Debug, "Iskender work item completed: kind {Kind}, attempt {Attempt}." },
        { 8006, "WorkItemRetryScheduled", LogLevel.Information, "Iskender work item retry scheduled: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}." },
        { 8007, "WorkItemPoisoned", LogLevel.Warning, "Iskender work item poisoned: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}." },
        { 8008, "WorkItemsSkewed", LogLevel.Warning, "Iskender work items skipped for version skew: kind {Kind}, outcome {Outcome}." },
        { 8009, "WorkDirectoryRowMalformed", LogLevel.Warning, "Iskender skipped a malformed scope directory row: module {Module}." },
        { 8010, "WorkApplyDiscarded", LogLevel.Information, "Iskender work item apply discarded: kind {Kind}, attempt {Attempt}, outcome {Outcome}." },
        { 8011, "WorkJobRun", LogLevel.Information, "Iskender work job run: job {Job}, outcome {Outcome}." },
        { 8012, "WorkPassFailed", LogLevel.Warning, "Iskender work pass failed: class {FailureClass}, code {Code}." },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void EachEventHasItsIdNameLevelTemplateAndOnlyAllowedFields(int id, string name, LogLevel level, string template)
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        var logged = Assert.Single(logger.Events, logged => logged.Id.Id == id);
        Assert.Equal((name, level, template), (logged.Id.Name, logged.Level, logged.Template));
        Assert.All(logged.Fields, field => Assert.Contains(field.Key, AllowedFields));
        Assert.Null(logged.Exception);
    }

    [Fact]
    public void TwelveEventsAreDefinedAndNoneRepeats()
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        Assert.Equal(Enumerable.Range(8001, 12), logger.Events.Select(logged => logged.Id.Id));
    }

    [Fact]
    public void TheEventsWriteTheirFixedOutcomesAndCodes()
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        var fields = logger.Events.ToDictionary(logged => logged.Id.Id, logged => logged.Fields.ToDictionary(field => field.Key, field => field.Value?.ToString()));
        Assert.Equal("drained", fields[8003]["Outcome"]);
        Assert.Equal(("routing.refresh", "created"), (fields[8004]["Kind"], fields[8004]["Outcome"]));
        Assert.Equal(("3", "transient", "persistence.timeout"), (fields[8006]["Attempt"], fields[8006]["FailureClass"], fields[8006]["Code"]));
        Assert.Equal("appeared", fields[8008]["Outcome"]);
        Assert.Equal("work", fields[8009]["Module"]);
        Assert.Equal("fencing_rejected", fields[8010]["Outcome"]);
        Assert.Equal(("work.purge_expired", "partial"), (fields[8011]["Job"], fields[8011]["Outcome"]));
    }

    [Fact]
    public void AnOutcomeOutsideTheFixedSetIsRefused()
    {
        var logger = new TemplateLogger();

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => WorkLog.ApplyDiscarded(logger, Kind, 1, "completed")).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => WorkLog.JobRun(logger, "work.retention", "drained")).Code);
        Assert.Empty(logger.Events);
    }

    public static TheoryData<string, string, string[]> Instruments() => new()
    {
        { "iskender.work.items.enqueued", "Counter", ["kind", "outcome"] },
        { "iskender.work.items.dispatched", "Counter", ["kind"] },
        { "iskender.work.items.completed", "Counter", ["kind"] },
        { "iskender.work.items.retried", "Counter", ["error_code", "failure_class", "kind"] },
        { "iskender.work.items.poisoned", "Counter", ["error_code", "failure_class", "kind"] },
        { "iskender.work.items.skewed", "ObservableGauge", ["kind"] },
        { "iskender.work.backlog", "ObservableGauge", ["kind"] },
        { "iskender.work.dispatch.duration", "Histogram", ["kind", "outcome"] },
        { "iskender.work.job.runs", "Counter", ["job", "outcome"] },
    };

    [Theory]
    [MemberData(nameof(Instruments))]
    public void EachInstrumentHasItsKindAndLabels(string name, string kind, string[] labels)
    {
        var published = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var recorded = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name == WorkTelemetry.SourceName)
                {
                    published[instrument.Name] = instrument.GetType().Name.Split('`')[0];
                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var keys = tags.ToArray().Select(tag => tag.Key).Order(StringComparer.Ordinal);
            recorded[instrument.Name] = string.Join(",", keys);
        }

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags));
        listener.Start();

        using var gauges = RecordEvery();
        listener.RecordObservableInstruments();

        Assert.Equal(kind, published[name]);
        Assert.Equal(string.Join(",", labels), recorded[name]);
        Assert.Equal(9, published.Count);
    }

    [Fact]
    public void TheDispatchActivityIsAConsumerWithNoParentAndOneLinkToTheRecordedContext()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        var recorded = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

        using var outer = new Activity("outer").Start();
        WorkTelemetry.End(WorkTelemetry.StartDispatch(Kind, 2, recorded), WorkOutcomes.Completed);
        WorkTelemetry.End(WorkTelemetry.StartDispatch(Kind, 1, recorded: null), WorkOutcomes.Completed);
        WorkTelemetry.End(WorkTelemetry.StartJob("work.retention"), WorkOutcomes.Held);

        var activities = stopped.Where(activity => activity.Source.Name == WorkTelemetry.SourceName).ToList();
        var linked = Assert.Single(activities, activity => activity.OperationName == "work.dispatch" && activity.Links.Any());
        Assert.Equal(ActivityKind.Consumer, linked.Kind);
        Assert.Equal(default, linked.ParentSpanId);
        Assert.NotEqual(outer.TraceId, linked.TraceId);
        Assert.Equal(recorded, Assert.Single(linked.Links).Context);
        Assert.Equal(["iskender.work.attempt", "iskender.work.kind", "iskender.work.outcome"], linked.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
        Assert.Single(activities, activity => activity.OperationName == "work.dispatch" && !activity.Links.Any());
        var job = Assert.Single(activities, activity => activity.OperationName == "work.job");
        Assert.Equal(["iskender.work.job", "iskender.work.outcome"], job.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
    }

    private static void WriteEvery(ILogger logger)
    {
        WorkLog.DispatcherStarted(logger);
        WorkLog.DispatcherStopping(logger);
        WorkLog.DispatcherStopped(logger, drained: true);
        WorkLog.ItemEnqueued(logger, Kind, created: true);
        WorkLog.ItemCompleted(logger, Kind, 1);
        WorkLog.RetryScheduled(logger, Kind, 3, Failure);
        WorkLog.ItemPoisoned(logger, Kind, 8, Failure);
        WorkLog.ItemsSkewed(logger, Kind, appeared: true);
        WorkLog.DirectoryRowMalformed(logger, "work");
        WorkLog.ApplyDiscarded(logger, Kind, 2, WorkOutcomes.FencingRejected);
        WorkLog.JobRun(logger, "work.purge_expired", WorkOutcomes.Partial);
        WorkLog.PassFailed(logger, WorkFailureClass.Transient, ErrorCodes.PersistenceUnavailable);
    }

    // The gauges belong to one dispatcher's WorkGauges, which the caller keeps until it has observed them.
    private static WorkGauges RecordEvery()
    {
        WorkTelemetry.RecordEnqueued(Kind, created: false);
        WorkTelemetry.RecordDispatched(Kind);
        WorkTelemetry.RecordCompleted(Kind);
        WorkTelemetry.RecordRetried(Kind, Failure);
        WorkTelemetry.RecordPoisoned(Kind, Failure);
        WorkTelemetry.RecordDispatchDuration(Kind, WorkOutcomes.Completed, TimeSpan.FromMilliseconds(12));
        WorkTelemetry.RecordJobRun("work.retention", WorkOutcomes.Completed);
        var gauges = new WorkGauges();
        gauges.SetSkewed(Kind, 2);
        gauges.SetBacklog(Kind, 5);
        return gauges;
    }

    private sealed record TemplateEvent(LogLevel Level, EventId Id, string Template, IReadOnlyList<KeyValuePair<string, object?>> Fields, Exception? Exception);

    // Keeps each event's template, from {OriginalFormat}, and its other fields.
    private sealed class TemplateLogger : ILogger
    {
        private readonly ConcurrentQueue<TemplateEvent> _events = new();

        public IReadOnlyList<TemplateEvent> Events => [.. _events];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            var template = fields.FirstOrDefault(field => field.Key == "{OriginalFormat}").Value as string ?? "";
            _events.Enqueue(new(logLevel, eventId, template, [.. fields.Where(field => field.Key != "{OriginalFormat}")], exception));
        }
    }
}

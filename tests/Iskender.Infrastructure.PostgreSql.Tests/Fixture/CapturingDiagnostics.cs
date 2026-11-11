using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>One captured log event: its level, ID, rendered message, structured fields, and exception.</summary>
internal sealed record CapturedLog(LogLevel Level, EventId Id, string Message, IReadOnlyList<KeyValuePair<string, object?>> Fields, Exception? Exception);

/// <summary>One stopped activity from <c>Iskender.Persistence</c>, with its tags.</summary>
internal sealed record CapturedActivity(string Name, IReadOnlyList<KeyValuePair<string, object?>> Tags);

/// <summary>One metric measurement from <c>Iskender.Persistence</c>, with its labels.</summary>
internal sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyList<KeyValuePair<string, object?>> Labels);

/// <summary>
/// Records every persistence log message and field, activity tag, and metric label the test assembly produces, for the
/// canary and label checks (SC-005). <see cref="Shared"/> listens for the whole run, and every <see cref="Logger"/> also
/// writes to it.
/// </summary>
internal sealed class CapturingDiagnostics : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;

    private CapturingDiagnostics()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PostgreSqlAdapter.DiagnosticsName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Enqueue(new CapturedActivity(activity.OperationName, [.. activity.TagObjects])),
        };
        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PostgreSqlAdapter.DiagnosticsName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meters.SetMeasurementEventCallback<double>((instrument, value, labels, _) => Measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.SetMeasurementEventCallback<long>((instrument, value, labels, _) => Measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.Start();
    }

    /// <summary>
    /// The canary (SC-005): a database name, both login names' prefix, both passwords, and a probe host in the tests that
    /// use it. It must never appear in anything the adapter reports.
    /// </summary>
    public const string Canary = "canaryq7z4x9v2";

    public static CapturingDiagnostics Shared { get; } = new();

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ConcurrentQueue<CapturedActivity> Activities { get; } = new();

    public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();

    /// <summary>Other text the adapter reported, such as probe results and exception messages, recorded by the tests.</summary>
    public ConcurrentQueue<string> Reported { get; } = new();

    /// <summary>Records <paramref name="exception"/>'s type, message, and inner exceptions, as a caller would see them.</summary>
    public void Report(Exception exception) => Reported.Enqueue(exception.ToString());

    /// <summary>Where the canary appears in anything captured so far: each entry names the kind of item, never its text.</summary>
    public IReadOnlyList<string> CanaryHits()
    {
        var hits = new List<string>();
        foreach (var log in Logs)
        {
            Check(hits, $"event {log.Id.Id} message", log.Message);
            Check(hits, $"event {log.Id.Id} exception", log.Exception?.ToString());
            foreach (var field in log.Fields)
            {
                Check(hits, $"event {log.Id.Id} field {field.Key}", $"{field.Key}={field.Value}");
            }
        }

        foreach (var activity in Activities)
        {
            Check(hits, "activity name", activity.Name);
            foreach (var tag in activity.Tags)
            {
                Check(hits, $"activity {activity.Name} tag", $"{tag.Key}={tag.Value}");
            }
        }

        foreach (var measurement in Measurements)
        {
            foreach (var label in measurement.Labels)
            {
                Check(hits, $"metric {measurement.Instrument} label", $"{label.Key}={label.Value}");
            }
        }

        foreach (var reported in Reported)
        {
            Check(hits, "reported result or exception", reported);
        }

        return hits;
    }

    /// <summary>Reads observable instruments, such as the connections gauge, into <see cref="Measurements"/>.</summary>
    public void Observe() => _meters.RecordObservableInstruments();

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    private static void Check(List<string> hits, string where, string? text)
    {
        if (text is not null && text.Contains(Canary, StringComparison.OrdinalIgnoreCase))
        {
            hits.Add(where);
        }
    }

    /// <summary>A logger that keeps its own events and also writes them to <see cref="Shared"/>.</summary>
    internal sealed class Logger : ILogger
    {
        private readonly ConcurrentQueue<CapturedLog> _events = new();

        public IReadOnlyList<CapturedLog> Events => [.. _events];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            var captured = new CapturedLog(logLevel, eventId, formatter(state, exception), [.. fields.Where(field => field.Key != "{OriginalFormat}")], exception);
            _events.Enqueue(captured);
            Shared.Logs.Enqueue(captured);
        }
    }
}

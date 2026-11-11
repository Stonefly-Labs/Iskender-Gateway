using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Iskender.Coordination.Conformance;

/// <summary>One captured log event: its level, ID, rendered message, structured fields, and exception.</summary>
public sealed record CapturedLog(LogLevel Level, EventId Id, string Message, IReadOnlyList<KeyValuePair<string, object?>> Fields, Exception? Exception);

/// <summary>One stopped activity from <c>Iskender.Coordination</c>, with its tags.</summary>
public sealed record CapturedActivity(string Name, IReadOnlyList<KeyValuePair<string, object?>> Tags);

/// <summary>One metric measurement from <c>Iskender.Coordination</c>, with its labels.</summary>
public sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyList<KeyValuePair<string, object?>> Labels);

/// <summary>
/// Records every coordination log message and field, activity tag, and metric label a test assembly produces, for the
/// canary and label checks (SC-005, FR-028), modeled on the persistence fixture's. <see cref="Shared"/> listens for the
/// whole run, and every <see cref="Logger"/> also writes to it.
/// </summary>
public sealed class CapturingDiagnostics : IDisposable
{
    /// <summary>The activity source and meter name every coordination diagnostic uses (research R18).</summary>
    public const string SourceName = "Iskender.Coordination";

    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;

    private CapturingDiagnostics()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Enqueue(new CapturedActivity(activity.OperationName, [.. activity.TagObjects])),
        };
        ActivitySource.AddActivityListener(_activities);

        _meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SourceName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meters.SetMeasurementEventCallback<double>((instrument, value, labels, _) => Measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.SetMeasurementEventCallback<long>((instrument, value, labels, _) => Measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.Start();
    }

    public static CapturingDiagnostics Shared { get; } = new();

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ConcurrentQueue<CapturedActivity> Activities { get; } = new();

    public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();

    /// <summary>Other text a coordinator reported, such as results, <c>ToString()</c> output, and exceptions, recorded by tests.</summary>
    public ConcurrentQueue<string> Reported { get; } = new();

    /// <summary>Records <paramref name="exception"/>'s type, message, diagnostics values, and inner exceptions, as a caller would see them.</summary>
    public void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Reported.Enqueue(exception.ToString());
        if (exception is Iskender.Core.Contracts.Errors.GatewayException { Error: { } error })
        {
            Reported.Enqueue(string.Join(";", error.Diagnostics.Values.Select(pair => $"{pair.Key}={pair.Value}")));
            Reported.Enqueue(error.Diagnostics.Text ?? "");
        }
    }

    /// <summary>Records a result's or object's text.</summary>
    public void Report(object? result) => Reported.Enqueue(result?.ToString() ?? "");

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

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    private static void Check(List<string> hits, string where, string? text)
    {
        if (Canary.In(text))
        {
            hits.Add(where);
        }
    }

    /// <summary>A logger that keeps its own events and also writes them to <see cref="Shared"/>.</summary>
    public sealed class Logger : ILogger
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

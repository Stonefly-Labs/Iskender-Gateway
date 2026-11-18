using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Work.Tests.Fixture;
using Microsoft.Extensions.Logging;

[assembly: AssemblyFixture(typeof(TelemetryCapture))]

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// The run-wide telemetry capture (ISK-0009 T055): for the whole test assembly, an <c>ActivityListener</c> and a
/// <c>MeterListener</c> over <c>Iskender.Work</c>, <c>Iskender.Persistence</c>, and <c>Iskender.Coordination</c>, and a
/// logger provider. With every fixture logger's events, which <see cref="CapturingDiagnostics.Shared"/> keeps, it records
/// each log message, field, and exception, span name and tag, and metric label, so a test can search all of them.
/// </summary>
public sealed class TelemetryCapture : ILoggerProvider
{
    private static readonly string[] Sources = ["Iskender.Work", "Iskender.Persistence", "Iskender.Coordination"];

    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;
    private readonly ConcurrentQueue<CapturedLog> _logs = new();
    private readonly ConcurrentQueue<CapturedActivity> _spans = new();
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();

    public TelemetryCapture()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => Sources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _spans.Enqueue(new CapturedActivity(activity.OperationName, [.. activity.TagObjects])),
        };
        ActivitySource.AddActivityListener(_activities);
        _meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (Sources.Contains(instrument.Meter.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meters.SetMeasurementEventCallback<long>((instrument, value, labels, _) => _measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.SetMeasurementEventCallback<double>((instrument, value, labels, _) => _measurements.Enqueue(new(instrument.Name, value, [.. labels.ToArray()])));
        _meters.Start();
    }

    /// <summary>The events written through this provider's loggers.</summary>
    internal IReadOnlyList<CapturedLog> Logs => [.. _logs];

    /// <summary>
    /// Where <paramref name="found"/> matches anything captured so far, the observable gauges included: each entry names
    /// the kind of item, never its text.
    /// </summary>
    public IReadOnlyList<string> Hits(Func<string, bool> found)
    {
        _meters.RecordObservableInstruments();
        var hits = new List<string>();
        void Check(string where, string? text)
        {
            if (text is not null && found(text))
            {
                hits.Add(where);
            }
        }

        foreach (var log in _logs.Concat(CapturingDiagnostics.Shared.Logs))
        {
            Check($"event {log.Id.Id} message", log.Message);
            Check($"event {log.Id.Id} exception", log.Exception?.ToString());
            foreach (var field in log.Fields)
            {
                Check($"event {log.Id.Id} field {field.Key}", $"{field.Key}={field.Value}");
            }
        }

        foreach (var span in _spans)
        {
            Check("span name", span.Name);
            foreach (var tag in span.Tags)
            {
                Check($"span {span.Name} tag", $"{tag.Key}={tag.Value}");
            }
        }

        foreach (var measurement in _measurements)
        {
            foreach (var label in measurement.Labels)
            {
                Check($"metric {measurement.Instrument} label", $"{label.Key}={label.Value}");
            }
        }

        foreach (var reported in CapturingDiagnostics.Shared.Reported)
        {
            Check("reported result or exception", reported);
        }

        return hits;
    }

    public ILogger CreateLogger(string categoryName) => new Logger(_logs);

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    private sealed class Logger(ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            logs.Enqueue(new CapturedLog(logLevel, eventId, formatter(state, exception), [.. fields.Where(field => field.Key != "{OriginalFormat}")], exception));
        }
    }
}

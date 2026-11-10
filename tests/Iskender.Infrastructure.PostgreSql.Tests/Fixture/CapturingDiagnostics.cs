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

    public static CapturingDiagnostics Shared { get; } = new();

    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ConcurrentQueue<CapturedActivity> Activities { get; } = new();

    public ConcurrentQueue<CapturedMeasurement> Measurements { get; } = new();

    /// <summary>Reads observable instruments, such as the connections gauge, into <see cref="Measurements"/>.</summary>
    public void Observe() => _meters.RecordObservableInstruments();

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
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

using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Testing;

/// <summary>One log entry: its category, event, level, rendered message, and structured values.</summary>
public sealed record CapturedLogEntry(string Category, EventId Event, LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)
{
    /// <summary>The structured value written under <paramref name="name"/>, as text.</summary>
    public string? Value(string name) => Values.TryGetValue(name, out var value) ? value?.ToString() : null;
}

/// <summary>
/// A logger provider that records every entry written through the host's logging, safely across threads (research
/// R17, hosting contract Test fixture rule 1). Owned by ISK-0006.
/// </summary>
public sealed class CapturedLog : ILoggerProvider
{
    private readonly Lock _gate = new();

    private readonly List<CapturedLogEntry> _entries = [];

    /// <summary>A copy of every entry so far, in the order written.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Every entry with event ID <paramref name="eventId"/>, in the order written.</summary>
    public IReadOnlyList<CapturedLogEntry> ById(int eventId) => [.. Entries.Where(entry => entry.Event.Id == eventId)];

    /// <summary>The one entry with event ID <paramref name="eventId"/>. Fails unless there is exactly one.</summary>
    public CapturedLogEntry Only(int eventId) => Assert.Single(ById(eventId));

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    private sealed class Logger(CapturedLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal)
                : [];
            log.Add(new(category, eventId, logLevel, formatter(state, exception), values));
        }
    }
}

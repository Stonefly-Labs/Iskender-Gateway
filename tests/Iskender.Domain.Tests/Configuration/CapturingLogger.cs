using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>One captured log event: its level, ID, rendered message, structured state, and exception.</summary>
internal sealed record CapturedEvent(LogLevel Level, EventId Id, string Message, IReadOnlyList<KeyValuePair<string, object?>> State, Exception? Exception);

/// <summary>An <see cref="ILogger"/> that keeps every event, so tests can check what was logged.</summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<CapturedEvent> _events = [];

    public IReadOnlyList<CapturedEvent> Events => _events;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        _events.Add(new CapturedEvent(logLevel, eventId, formatter(state, exception), [.. values], exception));
    }
}

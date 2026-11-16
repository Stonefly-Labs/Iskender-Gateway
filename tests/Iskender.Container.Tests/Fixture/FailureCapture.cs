using System.Collections.Concurrent;
using Iskender.Hosting.Testing;
using Microsoft.Extensions.Logging;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Forwards every entry to a <see cref="CapturedLog"/>, and also records each entry written with an exception, with the
/// exception, which <see cref="CapturedLog"/> does not keep. Only a failing test's message reads them, so an unexpected
/// readiness answer names the server's own failure (tasks T053).
/// </summary>
internal sealed class FailureCapture(CapturedLog log) : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _failures = new();

    /// <summary>Each entry written with an exception, as category, event ID, and the exception.</summary>
    public IReadOnlyCollection<string> Failures => _failures;

    public ILogger CreateLogger(string categoryName) => new Logger(this, log.CreateLogger(categoryName), categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(FailureCapture capture, ILogger inner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            if (exception is not null)
            {
                capture._failures.Enqueue($"{category}[{eventId.Id}] {exception}");
            }
        }
    }
}

using Iskender.Domain.Tests.Configuration;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary>An <see cref="ILoggerFactory"/> whose loggers all write to one <see cref="CapturingLogger"/>, with the category of each event.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public CapturingLogger Logger { get; } = new();

    public List<string> Categories { get; } = [];

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        Categories.Add(categoryName);
        return Logger;
    }

    public void Dispose()
    {
    }
}

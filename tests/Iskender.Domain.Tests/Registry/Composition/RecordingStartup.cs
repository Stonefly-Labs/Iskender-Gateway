using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary>An <see cref="ICompositionStartup"/> that records its start and stop in a shared journal.</summary>
internal sealed class RecordingStartup(string module, List<string> journal) : ICompositionStartup
{
    public string ModuleName => module;

    public ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        journal.Add($"start {module}");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        journal.Add($"stop {module}");
        return ValueTask.CompletedTask;
    }
}

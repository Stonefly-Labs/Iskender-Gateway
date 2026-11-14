using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Ports;

/// <summary>
/// Work a module does at host start, after validation, and at host stop. Hosting runs every registered one in ordinal
/// module-name order at start, and in reverse order at stop (hosting contract, Start-up rules 6 and 8). Owned by ISK-0006.
/// </summary>
public interface ICompositionStartup
{
    /// <summary>The name of the module this belongs to.</summary>
    string ModuleName { get; }

    ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

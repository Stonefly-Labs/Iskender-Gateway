using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Ports;

/// <summary>
/// The container's one validated configuration. Module services read their sections from <see cref="Snapshot"/>, which
/// validates on first need and then returns the same snapshot (hosting contract, Start-up rule 1). Owned by ISK-0006.
/// </summary>
public interface IValidatedConfiguration
{
    /// <summary>The validated snapshot. Fails with <c>hosting.configuration_rejected</c> when the configuration was rejected or could not be read.</summary>
    ConfigurationSnapshot Snapshot { get; }
}

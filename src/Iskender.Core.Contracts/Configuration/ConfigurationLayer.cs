using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// Where a configuration value came from, lowest precedence first. Rendered in text as <c>defaults</c>, <c>base</c>,
/// <c>overlay</c>, and <c>environment</c>.
/// </summary>
[ContractVersion("core-contracts", 2)]
public enum ConfigurationLayer
{
    /// <summary>A schema default, or no source at all.</summary>
    Defaults = 0,

    /// <summary>The base document.</summary>
    Base = 1,

    /// <summary>The overlay document for the active environment.</summary>
    Overlay = 2,

    /// <summary>An <c>ISKENDER__</c> environment variable.</summary>
    Environment = 3,
}

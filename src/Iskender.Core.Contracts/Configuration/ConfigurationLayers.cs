using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>The sources a snapshot was built from: the base and overlay source names, or none, and the count of environment variables applied.</summary>
[ContractVersion("core-contracts", 2)]
public sealed class ConfigurationLayers : IEquatable<ConfigurationLayers>
{
    public ConfigurationLayers(string? baseSource, string? overlaySource, int environmentVariableCount)
    {
        if (baseSource is "" || overlaySource is "" || environmentVariableCount < 0)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        BaseSource = baseSource;
        OverlaySource = overlaySource;
        EnvironmentVariableCount = environmentVariableCount;
    }

    /// <summary>The base document's source name, or null when none was used.</summary>
    public string? BaseSource { get; }

    /// <summary>The overlay document's source name, or null when none was used.</summary>
    public string? OverlaySource { get; }

    /// <summary>The number of environment variables applied.</summary>
    public int EnvironmentVariableCount { get; }

    public static bool operator ==(ConfigurationLayers? left, ConfigurationLayers? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ConfigurationLayers? left, ConfigurationLayers? right) => !(left == right);

    public bool Equals(ConfigurationLayers? other) =>
        other is not null && string.Equals(BaseSource, other.BaseSource, StringComparison.Ordinal)
            && string.Equals(OverlaySource, other.OverlaySource, StringComparison.Ordinal)
            && EnvironmentVariableCount == other.EnvironmentVariableCount;

    public override bool Equals(object? obj) => Equals(obj as ConfigurationLayers);

    public override int GetHashCode() =>
        HashCode.Combine(BaseSource is null ? 0 : StringComparer.Ordinal.GetHashCode(BaseSource),
            OverlaySource is null ? 0 : StringComparer.Ordinal.GetHashCode(OverlaySource), EnvironmentVariableCount);
}

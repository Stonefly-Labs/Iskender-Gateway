using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>Where a configuration value or error is: a source, a line and column, and a layer (FR-013).</summary>
[ContractVersion("core-contracts", 2)]
public sealed class ConfigurationSourceLocation : IEquatable<ConfigurationSourceLocation>
{
    public ConfigurationSourceLocation(string source, int line, int column, ConfigurationLayer layer)
    {
        if (string.IsNullOrEmpty(source) || line < 0 || column < 0 || !Enum.IsDefined(layer))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Source = source;
        Line = line;
        Column = column;
        Layer = layer;
    }

    /// <summary>The location of a value no source supplied: <c>(none)</c>, line 0, column 0, layer <c>defaults</c>.</summary>
    public static ConfigurationSourceLocation None { get; } = new(ConfigurationText.None, 0, 0, ConfigurationLayer.Defaults);

    /// <summary>The source name a host gave, an environment variable's name, or <c>(none)</c>.</summary>
    public string Source { get; }

    /// <summary>1-based, or 0 for an environment variable or <c>(none)</c>.</summary>
    public int Line { get; }

    /// <summary>1-based, in Unicode scalar values, or 0 for an environment variable or <c>(none)</c>.</summary>
    public int Column { get; }

    /// <summary>The layer the value came from.</summary>
    public ConfigurationLayer Layer { get; }

    public static bool operator ==(ConfigurationSourceLocation? left, ConfigurationSourceLocation? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ConfigurationSourceLocation? left, ConfigurationSourceLocation? right) => !(left == right);

    public bool Equals(ConfigurationSourceLocation? other) =>
        other is not null && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && Line == other.Line && Column == other.Column && Layer == other.Layer;

    public override bool Equals(object? obj) => Equals(obj as ConfigurationSourceLocation);

    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Source), Line, Column, Layer);

    /// <summary>Returns <c>&lt;source&gt;:&lt;line&gt;:&lt;column&gt;, &lt;layer&gt;</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Source}:{Line}:{Column}, {ConfigurationText.Layer(Layer)}");
}

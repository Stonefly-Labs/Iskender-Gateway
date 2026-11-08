using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>An opaque marker naming the configuration a request runs under. ISK-0057 decides what it names.</summary>
[ContractVersion("core-contracts", 1)]
public sealed class ConfigurationRevision : IEquatable<ConfigurationRevision>
{
    internal const int MaxLength = 64;

    public ConfigurationRevision(string value)
    {
        if (value is null || value.Length is < 1 or > MaxLength || !value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-'))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Value = value;
    }

    /// <summary>1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>, and <c>-</c>.</summary>
    public string Value { get; }

    public static bool operator ==(ConfigurationRevision? left, ConfigurationRevision? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ConfigurationRevision? left, ConfigurationRevision? right) => !(left == right);

    public bool Equals(ConfigurationRevision? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ConfigurationRevision);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

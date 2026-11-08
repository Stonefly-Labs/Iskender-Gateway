using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// An identifier a provider issued, such as an upstream request or conversation ID. It is never a <see cref="ResourceId"/>,
/// cannot be converted to or from one, and carries no owner (FR-013).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ProviderNativeId : IEquatable<ProviderNativeId>
{
    internal const int MaxProviderLength = 64;
    internal const int MaxValueLength = 256;

    public ProviderNativeId(string provider, string value)
    {
        if (provider is null || provider.Length is < 1 or > MaxProviderLength || provider.Any(char.IsControl)
            || value is null || value.Length is < 1 or > MaxValueLength || value.Any(char.IsControl))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Provider = provider;
        Value = value;
    }

    /// <summary>The issuing provider: 1 to 64 characters.</summary>
    public string Provider { get; }

    /// <summary>The provider's identifier: 1 to 256 characters, with no control characters.</summary>
    public string Value { get; }

    public bool Equals(ProviderNativeId? other) =>
        other is not null && string.Equals(Provider, other.Provider, StringComparison.Ordinal)
            && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ProviderNativeId);

    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Provider), StringComparer.Ordinal.GetHashCode(Value));

    public override string ToString() => Value;
}

using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// A secret reference placeholder: the name of a secret held elsewhere, never its value (TR-7). Nothing in this
/// package resolves it. ISK-0021 owns resolution.
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class SecretReference : IEquatable<SecretReference>
{
    internal const int MaxNameLength = 256;

    /// <summary>Fails with <c>config.secret.reference_malformed</c> for a malformed name. The failure holds no part of the name.</summary>
    public SecretReference(string name)
    {
        if (!IsValidName(name))
        {
            throw new GatewayException(ErrorCodes.ConfigSecretReferenceMalformed);
        }

        Name = name;
    }

    /// <summary>
    /// 1 to 256 characters of <c>a-zA-Z0-9._-/</c>, with no leading or trailing <c>/</c>, no empty segment, and no
    /// <c>..</c> segment.
    /// </summary>
    public string Name { get; }

    /// <summary>True when <paramref name="name"/> is a valid reference name.</summary>
    public static bool IsValidName(string? name)
    {
        if (name is null || name.Length is < 1 or > MaxNameLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-' or '/'))
            {
                return false;
            }
        }

        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    public static bool operator ==(SecretReference? left, SecretReference? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SecretReference? left, SecretReference? right) => !(left == right);

    public bool Equals(SecretReference? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as SecretReference);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <summary>Returns <c>secretRef:&lt;name&gt;</c>. The name is not a secret.</summary>
    public override string ToString() => "secretRef:" + Name;
}

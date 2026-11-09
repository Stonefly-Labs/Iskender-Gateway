using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>A registered namespace and the version of the schema that declares it.</summary>
[ContractVersion("core-contracts", 2)]
public sealed class SectionVersion : IEquatable<SectionVersion>
{
    public SectionVersion(string @namespace, int version)
    {
        if (string.IsNullOrEmpty(@namespace) || version < 1)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Namespace = @namespace;
        Version = version;
    }

    /// <summary>The namespace under <c>iskender</c>.</summary>
    public string Namespace { get; }

    /// <summary>The schema version, 1 or more.</summary>
    public int Version { get; }

    public static bool operator ==(SectionVersion? left, SectionVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SectionVersion? left, SectionVersion? right) => !(left == right);

    public bool Equals(SectionVersion? other) =>
        other is not null && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) && Version == other.Version;

    public override bool Equals(object? obj) => Equals(obj as SectionVersion);

    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Namespace), Version);

    /// <summary>Returns <c>&lt;namespace&gt;@&lt;version&gt;</c>.</summary>
    public override string ToString() => $"{Namespace}@{Version.ToString(CultureInfo.InvariantCulture)}";
}

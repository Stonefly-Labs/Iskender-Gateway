using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>A registered resource type. Instances are declared only in <see cref="ResourceTypes"/> (rule H5).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class ResourceType
{
    internal ResourceType(string name, string prefix, RegistryStatus status, int since)
    {
        Name = name;
        Prefix = prefix;
        Status = status;
        Since = since;
    }

    /// <summary>The type's name, such as <c>organization</c>.</summary>
    public string Name { get; }

    /// <summary>The ID prefix: 2 to 8 lowercase ASCII letters, unique, and never reassigned.</summary>
    public string Prefix { get; }

    /// <summary>Whether IDs of this type are still accepted.</summary>
    public RegistryStatus Status { get; }

    /// <summary>The <c>core-contracts</c> version that registered the type.</summary>
    public int Since { get; }

    public override string ToString() => Name;
}

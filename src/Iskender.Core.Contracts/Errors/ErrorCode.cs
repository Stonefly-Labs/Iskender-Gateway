using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>A registered error code. Instances are declared only in <see cref="ErrorCodes"/> (rule H5).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class ErrorCode
{
    internal ErrorCode(string id, ErrorCategory category, string meaning, string safeMessage, RegistryStatus status, int since)
    {
        Id = id;
        Category = category;
        Meaning = meaning;
        SafeMessage = safeMessage;
        Status = status;
        Since = since;
    }

    /// <summary>The code, lower case and dot-namespaced by owning module, such as <c>core.resource_id.malformed</c>.</summary>
    public string Id { get; }

    /// <summary>The one category the code belongs to.</summary>
    public ErrorCategory Category { get; }

    /// <summary>What the code means, for contract readers. Never shown to a client.</summary>
    public string Meaning { get; }

    /// <summary>The fixed client-visible message. It never carries request data.</summary>
    public string SafeMessage { get; }

    /// <summary>Whether the code is still accepted.</summary>
    public RegistryStatus Status { get; }

    /// <summary>The <c>core-contracts</c> version that registered the code.</summary>
    public int Since { get; }

    public override string ToString() => Id;
}

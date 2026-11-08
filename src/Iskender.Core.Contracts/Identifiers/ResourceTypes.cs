using System.Diagnostics.CodeAnalysis;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// The resource type registry: the only place a <see cref="ResourceType"/> is declared (rule H5). The published
/// contract's Resource types table must match it row for row (rule H4).
/// </summary>
[ContractVersion("core-contracts", 1)]
public static class ResourceTypes
{
    /// <summary>The prefix of <c>RequestCorrelationId</c>, which is never a resource type.</summary>
    internal const string ReservedRequestPrefix = "req";

    public static readonly ResourceType Organization = Active("organization", "org");
    public static readonly ResourceType Project = Active("project", "prj");
    public static readonly ResourceType Endpoint = Active("endpoint", "ep");
    public static readonly ResourceType ModelAlias = Active("model alias", "alias");
    public static readonly ResourceType BackendPool = Active("backend pool", "pool");
    public static readonly ResourceType Deployment = Active("deployment", "dep");
    public static readonly ResourceType CredentialProfile = Active("credential profile", "cred");
    public static readonly ResourceType ApiKey = Active("API key", "key");
    public static readonly ResourceType PolicyDefinition = Active("policy definition", "poldef");
    public static readonly ResourceType PolicyAssignment = Active("policy assignment", "polasgn");
    public static readonly ResourceType Budget = Active("budget", "budget");
    public static readonly ResourceType Conversation = Active("conversation", "conv");
    public static readonly ResourceType AuditEntry = Active("audit entry", "audit");

    /// <summary>Every registered type, in contract order.</summary>
    public static IReadOnlyList<ResourceType> All { get; } = Validate(
    [
        Organization, Project, Endpoint, ModelAlias, BackendPool, Deployment, CredentialProfile, ApiKey,
        PolicyDefinition, PolicyAssignment, Budget, Conversation, AuditEntry,
    ]);

    /// <summary>Finds a registered type by its prefix, compared ordinally.</summary>
    public static bool TryGetByPrefix(string prefix, [NotNullWhen(true)] out ResourceType? type) => TryGetByPrefix(All, prefix, out type);

    internal static bool TryGetByPrefix(IReadOnlyList<ResourceType> registry, ReadOnlySpan<char> prefix, [NotNullWhen(true)] out ResourceType? type)
    {
        foreach (var candidate in registry)
        {
            if (prefix.SequenceEqual(candidate.Prefix))
            {
                type = candidate;
                return true;
            }
        }

        type = null;
        return false;
    }

    /// <summary>Rejects a duplicate, invalid, or reserved prefix.</summary>
    internal static IReadOnlyList<ResourceType> Validate(ResourceType[] types)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!IsValidPrefix(type.Prefix))
            {
                throw new InvalidOperationException($"Resource type prefix '{type.Prefix}' is not 2 to 8 lowercase ASCII letters.");
            }

            if (string.Equals(type.Prefix, ReservedRequestPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Resource type prefix '{type.Prefix}' is reserved for the request correlation ID.");
            }

            if (!seen.Add(type.Prefix))
            {
                throw new InvalidOperationException($"Resource type prefix '{type.Prefix}' is registered twice.");
            }
        }

        return Array.AsReadOnly(types);
    }

    internal static bool IsValidPrefix(string prefix) =>
        prefix.Length is >= 2 and <= 8 && prefix.All(c => c is >= 'a' and <= 'z');

    private static ResourceType Active(string name, string prefix) => new(name, prefix, RegistryStatus.Active, since: 1);
}

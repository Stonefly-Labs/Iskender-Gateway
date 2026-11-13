using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Coordination.Conformance;

/// <summary>
/// The coordination canary (SC-005): one value planted as the Redis host alias, the ACL user name, the password, an
/// organization ID, and a cached value. It must appear 0 times in anything the coordinators report or store as a key.
/// </summary>
public static class Canary
{
    /// <summary>Lowercase letters and digits only, so it is a valid host alias, user name, and Crockford base32 text.</summary>
    public const string Value = "canaryk5w8m3t1";

    /// <summary>An organization ID whose body contains <see cref="Value"/>.</summary>
    public static ResourceId OrganizationId { get; } = ResourceId.Parse("org_0" + Value + "00000000000", ResourceTypes.Organization);

    /// <summary><see cref="Value"/> as UTF-8 bytes, for a cached value.</summary>
    public static byte[] Bytes => System.Text.Encoding.UTF8.GetBytes(Value);

    /// <summary>Whether <paramref name="text"/> contains the canary, ignoring case.</summary>
    public static bool In(string? text) => text is not null && text.Contains(Value, StringComparison.OrdinalIgnoreCase);
}

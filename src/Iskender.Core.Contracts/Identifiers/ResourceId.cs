using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// An opaque resource ID: <c>&lt;prefix&gt;_&lt;26 base32&gt;</c>, at most 35 characters, whose body is 128 bits from a
/// cryptographically secure source and encodes no owner, time, sequence, or host (FR-005, FR-006, research R6).
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ResourceId : IEquatable<ResourceId>
{
    internal const int MaxLength = 35;
    internal const int ExcerptLength = 16;

    private ResourceId(ResourceType type, string value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>Fills a buffer with random bytes. The production source is <see cref="RandomNumberGenerator.Fill"/>.</summary>
    internal delegate void RandomFill(Span<byte> buffer);

    /// <summary>The resource type the prefix names.</summary>
    public ResourceType Type { get; }

    /// <summary>The canonical text.</summary>
    public string Value { get; }

    /// <summary>Generates a new ID of an active type. Fails with <c>core.random_source_failed</c> if the secure source fails, with no fallback.</summary>
    public static ResourceId New(ResourceType type) => New(type, ResourceTypes.All, RandomNumberGenerator.Fill);

    /// <summary>Parses canonical text as the expected type, or throws a <see cref="GatewayException"/> with a <c>core.resource_id.*</c> code.</summary>
    public static ResourceId Parse(string text, ResourceType expected) => Parse(text, expected, ResourceTypes.All);

    /// <summary>Parses canonical text as the expected type. On failure, <paramref name="id"/> is null and <paramref name="error"/> names the reason.</summary>
    public static bool TryParse(
        string? text,
        ResourceType expected,
        [NotNullWhen(true)] out ResourceId? id,
        [NotNullWhen(false)] out ErrorCode? error) =>
        TryParse(text, expected, ResourceTypes.All, out id, out error);

    /// <summary>Test seam: generates against a supplied registry and random source.</summary>
    internal static ResourceId New(ResourceType type, IReadOnlyList<ResourceType> registry, RandomFill fill)
    {
        if (!registry.Contains(type))
        {
            throw new GatewayException(ErrorCodes.ResourceIdUnknownType);
        }

        if (type.Status == RegistryStatus.Retired)
        {
            throw new GatewayException(ErrorCodes.ResourceIdRetiredType);
        }

        return new ResourceId(type, $"{type.Prefix}_{NewBody(fill)}");
    }

    /// <summary>Test seam: parses against a supplied registry.</summary>
    internal static ResourceId Parse(string text, ResourceType expected, IReadOnlyList<ResourceType> registry)
    {
        if (TryParse(text, expected, registry, out var id, out var error))
        {
            return id;
        }

        throw new GatewayException(error, text is null ? null : $"Input begins \"{Excerpt(text)}\".");
    }

    internal static bool TryParse(
        string? text,
        ResourceType expected,
        IReadOnlyList<ResourceType> registry,
        [NotNullWhen(true)] out ResourceId? id,
        [NotNullWhen(false)] out ErrorCode? error)
    {
        id = null;
        error = Check(text, expected, registry, out var type);
        if (error is not null)
        {
            return false;
        }

        id = new ResourceId(type!, text!);
        return true;
    }

    /// <summary>26 base32 characters from 16 secure random bytes. Shared with the request correlation ID.</summary>
    internal static string NewBody(RandomFill fill)
    {
        Span<byte> bytes = stackalloc byte[CrockfordBase32.ByteCount];
        try
        {
            fill(bytes);
        }
        catch (CryptographicException exception)
        {
            throw new GatewayException(ErrorCodes.RandomSourceFailed, detail: null, exception);
        }

        return CrockfordBase32.Encode(bytes);
    }

    /// <summary>At most 16 characters of the input, with control characters and surrogates replaced, for an error message.</summary>
    internal static string Excerpt(string text)
    {
        var length = Math.Min(text.Length, ExcerptLength);
        return string.Create(length, text, static (chars, source) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                var c = source[i];
                chars[i] = char.IsControl(c) || char.IsSurrogate(c) ? '?' : c;
            }
        });
    }

    // Checks in order, stopping at the first failure: shape, prefix registered, prefix active, expected type, body.
    private static ErrorCode? Check(string? text, ResourceType expected, IReadOnlyList<ResourceType> registry, out ResourceType? type)
    {
        type = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return ErrorCodes.ResourceIdMalformed;
        }

        var separator = text.IndexOf('_', StringComparison.Ordinal);
        if (separator < 0)
        {
            return ErrorCodes.ResourceIdMalformed;
        }

        if (!ResourceTypes.TryGetByPrefix(registry, text.AsSpan(0, separator), out type))
        {
            return ErrorCodes.ResourceIdUnknownType;
        }

        if (type!.Status == RegistryStatus.Retired)
        {
            return ErrorCodes.ResourceIdRetiredType;
        }

        if (!ReferenceEquals(type, expected))
        {
            return ErrorCodes.ResourceIdWrongType;
        }

        if (CrockfordBase32.Check(text.AsSpan(separator + 1)) != CrockfordBase32.BodyCheck.Canonical)
        {
            return ErrorCodes.ResourceIdNonCanonical;
        }

        return null;
    }

    public static bool operator ==(ResourceId? left, ResourceId? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ResourceId? left, ResourceId? right) => !(left == right);

    public bool Equals(ResourceId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ResourceId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

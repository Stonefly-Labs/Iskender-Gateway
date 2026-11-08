using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// What a provider said about a provider-originated error. A provider's own error code stays here and is never promoted
/// to <see cref="GatewayError.Code"/>.
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ProviderErrorMetadata
{
    internal const int MaxLength = 64;

    public ProviderErrorMetadata(string provider, ProviderNativeId? providerRequestId = null, string? providerErrorCode = null)
    {
        if (provider is null || provider.Length < 1 || !IsToken(provider) || (providerErrorCode is not null && !IsToken(providerErrorCode)))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Provider = provider;
        ProviderRequestId = providerRequestId;
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>The provider: 1 to 64 characters of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>-</c>.</summary>
    public string Provider { get; }

    /// <summary>The provider's request ID, when it gave one.</summary>
    public ProviderNativeId? ProviderRequestId { get; }

    /// <summary>The provider's error code: at most 64 characters of the same set, or null.</summary>
    public string? ProviderErrorCode { get; }

    private static bool IsToken(string value) =>
        value.Length <= MaxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

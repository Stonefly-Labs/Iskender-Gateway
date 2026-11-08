using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// An error envelope as read from its wire form, by <see cref="ErrorEnvelopeCodec.Read"/>. Each property is one field of
/// the version 1 format in the published contract. Codes and categories stay as text, since a reader accepts every
/// supported version.
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class ErrorEnvelope
{
    internal ErrorEnvelope(
        int schemaVersion,
        string code,
        string category,
        string message,
        string correlationId,
        bool hasRetry,
        long? retryAfterSeconds,
        string? providerName,
        string? providerRequestId,
        string? providerErrorCode,
        RedactionStatus redactionStatus)
    {
        SchemaVersion = schemaVersion;
        Code = code;
        Category = category;
        Message = message;
        CorrelationId = correlationId;
        HasRetry = hasRetry;
        RetryAfterSeconds = retryAfterSeconds;
        ProviderName = providerName;
        ProviderRequestId = providerRequestId;
        ProviderErrorCode = providerErrorCode;
        RedactionStatus = redactionStatus;
    }

    /// <summary><c>schema_version</c>.</summary>
    public int SchemaVersion { get; }

    /// <summary><c>code</c>.</summary>
    public string Code { get; }

    /// <summary><c>category</c>.</summary>
    public string Category { get; }

    /// <summary><c>message</c>.</summary>
    public string Message { get; }

    /// <summary><c>correlation_id</c>.</summary>
    public string CorrelationId { get; }

    /// <summary>Whether the <c>retry</c> object was present.</summary>
    public bool HasRetry { get; }

    /// <summary><c>retry.after_seconds</c>, or null.</summary>
    public long? RetryAfterSeconds { get; }

    /// <summary><c>provider.name</c>, or null when there was no <c>provider</c> object.</summary>
    public string? ProviderName { get; }

    /// <summary><c>provider.request_id</c>, or null.</summary>
    public string? ProviderRequestId { get; }

    /// <summary><c>provider.error_code</c>, or null.</summary>
    public string? ProviderErrorCode { get; }

    /// <summary><c>redaction_status</c>.</summary>
    public RedactionStatus RedactionStatus { get; }
}

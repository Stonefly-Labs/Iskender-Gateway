using System.Buffers;
using System.Text.Json;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// The <c>error-envelope</c> wire contract: UTF-8 JSON, no indentation, no trailing newline, in the published field
/// order. Hand-written so the output is exact (research R11). The writer never reads <see cref="GatewayError.Diagnostics"/>.
/// </summary>
[ContractVersion("core-contracts", 1)]
public static class ErrorEnvelopeCodec
{
    private const int MaxClientRequestIdLength = 128;

    /// <summary>Writes the current version of the envelope.</summary>
    public static byte[] Write(GatewayError error)
    {
        if (error is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", WireContracts.ErrorEnvelope.CurrentVersion);
            writer.WriteString("code", error.Code.Id);
            writer.WriteString("category", error.Category.Id);
            writer.WriteString("message", error.SafeMessage);
            writer.WriteString("correlation_id", error.CorrelationId.Value);

            if (error.Retry is { } retry)
            {
                writer.WriteStartObject("retry");
                if (retry.RetryAfter is { } after)
                {
                    // Rounded up by remainder, not by adding a second's ticks first, so TimeSpan.MaxValue cannot overflow.
                    writer.WriteNumber("after_seconds", (after.Ticks / TimeSpan.TicksPerSecond) + (after.Ticks % TimeSpan.TicksPerSecond == 0 ? 0 : 1));
                }

                writer.WriteEndObject();
            }

            if (error.Provider is { } provider)
            {
                writer.WriteStartObject("provider");
                writer.WriteString("name", provider.Provider);
                // A provider ID may hold any text a provider sent; only a bounded token form reaches a client.
                if (provider.ProviderRequestId is { } requestId && IsClientToken(requestId.Value))
                {
                    writer.WriteString("request_id", requestId.Value);
                }

                if (provider.ProviderErrorCode is { } errorCode)
                {
                    writer.WriteString("error_code", errorCode);
                }

                writer.WriteEndObject();
            }

            writer.WriteString("redaction_status", error.Redaction == RedactionStatus.Withheld ? "withheld" : "none");
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads any supported version. Unknown fields are ignored. A missing required field, a wrong type, or an unsupported
    /// <c>schema_version</c> fails with <c>core.value.invalid</c>.
    /// </summary>
    public static ErrorEnvelope Read(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException exception)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid, detail: null, exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid();
            }

            var version = RequiredInt(root, "schema_version");
            if (!WireContracts.ErrorEnvelope.SupportedVersions.Contains(version))
            {
                throw Invalid();
            }

            var hasRetry = false;
            long? afterSeconds = null;
            if (root.TryGetProperty("retry", out var retry))
            {
                if (retry.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid();
                }

                hasRetry = true;
                if (retry.TryGetProperty("after_seconds", out var after))
                {
                    if (after.ValueKind != JsonValueKind.Number || !after.TryGetInt64(out var seconds) || seconds < 0)
                    {
                        throw Invalid();
                    }

                    afterSeconds = seconds;
                }
            }

            string? providerName = null, requestId = null, errorCode = null;
            if (root.TryGetProperty("provider", out var provider))
            {
                if (provider.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid();
                }

                providerName = RequiredString(provider, "name");
                requestId = OptionalString(provider, "request_id");
                errorCode = OptionalString(provider, "error_code");
            }

            var redaction = RequiredString(root, "redaction_status") switch
            {
                "none" => RedactionStatus.None,
                "withheld" => RedactionStatus.Withheld,
                _ => throw Invalid(),
            };

            return new ErrorEnvelope(
                version,
                RequiredString(root, "code"),
                RequiredString(root, "category"),
                RequiredString(root, "message"),
                RequiredString(root, "correlation_id"),
                hasRetry,
                afterSeconds,
                providerName,
                requestId,
                errorCode,
                redaction);
        }
    }

    private static int RequiredInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw Invalid();

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();

    private static string? OptionalString(JsonElement parent, string name) =>
        !parent.TryGetProperty(name, out var value) ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : throw Invalid();

    private static GatewayException Invalid() => new(ErrorCodes.ValueInvalid);

    /// <summary>1 to 128 characters of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>:</c>, <c>-</c>.</summary>
    private static bool IsClientToken(string value) =>
        value.Length is >= 1 and <= MaxClientRequestIdLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-');
}

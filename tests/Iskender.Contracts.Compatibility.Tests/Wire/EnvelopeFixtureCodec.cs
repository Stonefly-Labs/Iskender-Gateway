using System.Globalization;
using System.Text.Json;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Contracts.Compatibility.Tests.Wire;

/// <summary>How the wire checks write and read an envelope. The real one wraps <see cref="ErrorEnvelopeCodec"/>.</summary>
public interface IEnvelopeCodec
{
    /// <summary>Builds the error an <c>.input.json</c> describes and writes it.</summary>
    byte[] Write(JsonElement input);

    /// <summary>Reads an envelope into the flat field map an <c>.read.json</c> holds.</summary>
    IReadOnlyDictionary<string, string?> Read(byte[] envelope);
}

/// <summary>
/// The real codec. Every fixture uses the all-zero correlation ID, <c>req_00000000000000000000000000</c>, so that the
/// expected bytes are fixed.
/// </summary>
public sealed class EnvelopeFixtureCodec : IEnvelopeCodec
{
    public static readonly EnvelopeFixtureCodec Instance = new();

    public byte[] Write(JsonElement input)
    {
        var code = ErrorCodes.TryGet(input.GetProperty("code").GetString()!, out var registered)
            ? registered
            : throw new InvalidDataException($"Unregistered code in fixture input: {input.GetProperty("code")}");
        var visibility = input.TryGetProperty("visibility", out var v) && v.GetString() == "visible" ? OutputVisibility.Visible : OutputVisibility.None;

        RetryAdvice? retry = null;
        if (input.TryGetProperty("retry", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            TimeSpan? after = r.TryGetProperty("after_ms", out var ms) ? TimeSpan.FromMilliseconds(ms.GetInt64()) : null;
            retry = new RetryAdvice(after, r.TryGetProperty("retriable", out var retriable) && retriable.GetBoolean());
        }

        ProviderErrorMetadata? provider = null;
        if (input.TryGetProperty("provider", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            var name = p.GetProperty("name").GetString()!;
            var requestId = p.TryGetProperty("request_id", out var id) ? new ProviderNativeId(name, id.GetString()!) : null;
            provider = new ProviderErrorMetadata(name, requestId, p.TryGetProperty("error_code", out var e) ? e.GetString() : null);
        }

        var diagnostics = input.TryGetProperty("diagnostics", out var d) && d.ValueKind == JsonValueKind.String ? new ErrorDiagnostics(d.GetString()) : null;
        var correlation = RequestCorrelationId.New(buffer => buffer.Clear());

        return ErrorEnvelopeCodec.Write(GatewayError.Create(code, correlation, visibility, retry, provider, diagnostics));
    }

    public IReadOnlyDictionary<string, string?> Read(byte[] envelope)
    {
        var read = ErrorEnvelopeCodec.Read(envelope);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["schema_version"] = read.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            ["code"] = read.Code,
            ["category"] = read.Category,
            ["message"] = read.Message,
            ["correlation_id"] = read.CorrelationId,
            ["has_retry"] = read.HasRetry ? "true" : "false",
            ["retry_after_seconds"] = read.RetryAfterSeconds?.ToString(CultureInfo.InvariantCulture),
            ["provider_name"] = read.ProviderName,
            ["provider_request_id"] = read.ProviderRequestId,
            ["provider_error_code"] = read.ProviderErrorCode,
            ["redaction_status"] = read.RedactionStatus == RedactionStatus.Withheld ? "withheld" : "none",
        };
    }

    /// <summary>A <c>.read.json</c> as a flat field map: strings as themselves, other values as their JSON text, null as null.</summary>
    public static IReadOnlyDictionary<string, string?> Flatten(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            },
            StringComparer.Ordinal);
    }
}

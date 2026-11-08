using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Context;

/// <summary>
/// The correlation values of one request: the gateway's own request ID, which is authoritative, the trace and span IDs,
/// and a caller-supplied value, kept only if it has the bounded form and otherwise dropped whole (FR-016, research R12).
/// The caller value is never a key, a log template, or an authorization input.
/// </summary>
[ContractVersion("core-contracts", 1)]
public sealed class CorrelationContext
{
    internal const int MaxCallerValueLength = 128;

    public CorrelationContext(RequestCorrelationId requestId, ActivityTraceId traceId, ActivitySpanId spanId, string? callerValue = null)
    {
        RequestId = requestId ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        TraceId = traceId;
        SpanId = spanId;

        if (callerValue is null)
        {
            return;
        }

        if (IsBounded(callerValue))
        {
            CallerValue = callerValue;
        }
        else
        {
            CallerValueDropped = true;
        }
    }

    /// <summary>The gateway's request ID.</summary>
    public RequestCorrelationId RequestId { get; }

    /// <summary>The W3C trace ID.</summary>
    public ActivityTraceId TraceId { get; }

    /// <summary>The W3C span ID.</summary>
    public ActivitySpanId SpanId { get; }

    /// <summary>The caller's value: 1 to 128 characters of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>:</c>, <c>-</c>, or null.</summary>
    public string? CallerValue { get; }

    /// <summary>True when the caller sent a value outside the bounded form and it was dropped.</summary>
    public bool CallerValueDropped { get; }

    private static bool IsBounded(string value) =>
        value.Length is >= 1 and <= MaxCallerValueLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-');
}

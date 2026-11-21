using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>
/// A request to enqueue one work item through <see cref="IWorkOutbox"/> (work contract, Work items and The outbox). Its
/// <see cref="ToString"/> shows the kind only, so no key or payload byte reaches a log by formatting. Owned by ISK-0009.
/// </summary>
public sealed record WorkItemRequest
{
    /// <summary>
    /// A <c>null</c> owner or kind, a payload version outside 1 to 1,000, an idempotency key that is not 1 to 128
    /// characters of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c>, <c>:</c>, and <c>-</c>, or a delay outside
    /// 0 to 30 days fails with <c>core.value.invalid</c>. The payload's size is checked by the outbox.
    /// </summary>
    public WorkItemRequest(OwnerScope owner, WorkKind kind, int payloadVersion, ReadOnlyMemory<byte> payload, string idempotencyKey, TimeSpan delay = default)
    {
        Owner = owner ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        Kind = kind ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        PayloadVersion = WorkLimits.RequirePayloadVersion(payloadVersion);
        Payload = payload;
        IdempotencyKey = WorkLimits.RequireIdempotencyKey(idempotencyKey);
        Delay = delay >= TimeSpan.Zero && delay <= WorkLimits.MaximumDelay ? delay : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    /// <summary>The scope that owns the item, which must be the enqueuing unit's.</summary>
    public OwnerScope Owner { get; }

    public WorkKind Kind { get; }

    public int PayloadVersion { get; }

    /// <summary>The producing module's serialized payload, at most 65,536 bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Unique per scope and kind among retained items.</summary>
    public string IdempotencyKey { get; }

    /// <summary>How long after the enqueue the item becomes due, on the database's clock.</summary>
    public TimeSpan Delay { get; }

    public override string ToString() => $"WorkItemRequest {{ Kind = {Kind} }}";
}

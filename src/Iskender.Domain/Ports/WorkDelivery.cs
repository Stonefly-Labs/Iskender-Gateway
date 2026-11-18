using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Ports;

/// <summary>
/// One attempt's delivery of a work item to its handler (work contract, Dispatch and apply). Its <see cref="ToString"/>
/// shows the kind and attempt only, so no ID, key, or payload byte reaches a log by formatting. Owned by ISK-0009.
/// </summary>
public sealed class WorkDelivery
{
    /// <summary>A <c>null</c> owner or kind, a payload version outside 1 to 1,000, or an attempt below 1 fails with <c>core.value.invalid</c>.</summary>
    public WorkDelivery(Guid itemId, OwnerScope owner, WorkKind kind, int payloadVersion, ReadOnlyMemory<byte> payload, string idempotencyKey, int attempt)
    {
        ItemId = itemId;
        Owner = owner ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        Kind = kind ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        PayloadVersion = WorkLimits.RequirePayloadVersion(payloadVersion);
        Payload = payload;
        IdempotencyKey = WorkLimits.RequireIdempotencyKey(idempotencyKey);
        Attempt = attempt >= 1 ? attempt : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    /// <summary>The item's opaque ID, with <see cref="IdempotencyKey"/> the key for a handler's external idempotency.</summary>
    public Guid ItemId { get; }

    public OwnerScope Owner { get; }

    public WorkKind Kind { get; }

    public int PayloadVersion { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public string IdempotencyKey { get; }

    /// <summary>This attempt's number, from 1.</summary>
    public int Attempt { get; }

    public override string ToString() => $"WorkDelivery {{ Kind = {Kind}, Attempt = {Attempt} }}";
}

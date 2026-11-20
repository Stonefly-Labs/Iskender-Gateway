using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The registration of a kind's one handler (work contract, Registration rule 3). <see cref="MaximumAttempts"/> and
/// <see cref="Timeout"/> replace <c>work.retry.maximumAttempts</c> and <c>work.handler.timeout</c> for the kind when set.
/// Owned by ISK-0009.
/// </summary>
public sealed record WorkHandlerRegistration
{
    private static readonly TimeSpan ShortestTimeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestTimeout = TimeSpan.FromHours(1);

    /// <summary>
    /// Payload versions 1 to 1,000 with lowest at most highest; <paramref name="maximumAttempts"/> 1 to 100;
    /// <paramref name="timeout"/> 1 s to 1 h; <paramref name="handlerType"/> a concrete class implementing
    /// <see cref="IWorkHandler"/>. Otherwise <c>core.value.invalid</c>.
    /// </summary>
    public WorkHandlerRegistration(
        WorkKind kind,
        int lowestPayloadVersion,
        int highestPayloadVersion,
        int? maximumAttempts,
        TimeSpan? timeout,
        bool hasExternalEffects,
        Type handlerType)
    {
        Kind = kind ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        LowestPayloadVersion = WorkLimits.RequirePayloadVersion(lowestPayloadVersion);
        HighestPayloadVersion = WorkLimits.RequirePayloadVersion(highestPayloadVersion);
        if (lowestPayloadVersion > highestPayloadVersion
            || maximumAttempts is < 1 or > 100
            || (timeout is { } value && (value < ShortestTimeout || value > LongestTimeout))
            || handlerType is null || !handlerType.IsClass || handlerType.IsAbstract || !typeof(IWorkHandler).IsAssignableFrom(handlerType))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        MaximumAttempts = maximumAttempts;
        Timeout = timeout;
        HasExternalEffects = hasExternalEffects;
        HandlerType = handlerType;
    }

    public WorkKind Kind { get; }

    public int LowestPayloadVersion { get; }

    public int HighestPayloadVersion { get; }

    public int? MaximumAttempts { get; }

    public TimeSpan? Timeout { get; }

    /// <summary>The handler has an effect outside its unit, which it makes idempotent itself (Dispatch and apply rule 9).</summary>
    public bool HasExternalEffects { get; }

    public Type HandlerType { get; }
}

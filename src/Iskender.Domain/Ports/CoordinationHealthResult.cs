using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A status and one reason, and nothing else: no host, user, key, or server text (FR-026). Healthy occurs only with
/// <see cref="CoordinationHealthReason.Ok"/>, or with <see cref="CoordinationHealthReason.NotConfigured"/> when nothing
/// distributed is in use, and <c>Ok</c> only with healthy. Owned by ISK-0005.
/// </summary>
public sealed record CoordinationHealthResult
{
    /// <summary>A pairing that breaks those rules fails with <c>core.value.invalid</c>.</summary>
    public CoordinationHealthResult(CoordinationHealthStatus status, CoordinationHealthReason reason)
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(reason)
            || (status == CoordinationHealthStatus.Healthy && reason is not (CoordinationHealthReason.Ok or CoordinationHealthReason.NotConfigured))
            || (status == CoordinationHealthStatus.Unhealthy && reason == CoordinationHealthReason.Ok))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Status = status;
        Reason = reason;
    }

    /// <summary>Every distributed coordinator in use answers.</summary>
    public static CoordinationHealthResult Healthy { get; } = new(CoordinationHealthStatus.Healthy, CoordinationHealthReason.Ok);

    /// <summary>No primitive uses a distributed coordinator.</summary>
    public static CoordinationHealthResult NothingDistributed { get; } = new(CoordinationHealthStatus.Healthy, CoordinationHealthReason.NotConfigured);

    public CoordinationHealthStatus Status { get; }

    public CoordinationHealthReason Reason { get; }

    /// <summary>An unhealthy result with <paramref name="reason"/>.</summary>
    public static CoordinationHealthResult Unhealthy(CoordinationHealthReason reason) => new(CoordinationHealthStatus.Unhealthy, reason);
}

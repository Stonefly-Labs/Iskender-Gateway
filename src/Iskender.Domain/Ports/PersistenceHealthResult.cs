using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A status and one reason, and nothing else: no host, user, database name, or server text (FR-026). Healthy occurs only
/// with <see cref="PersistenceHealthReason.Ok"/>, and <c>Ok</c> only with healthy. Owned by ISK-0004.
/// </summary>
public sealed record PersistenceHealthResult
{
    /// <summary>A pairing that breaks the healthy-only-with-ok rule fails with <c>core.value.invalid</c>.</summary>
    public PersistenceHealthResult(PersistenceHealthStatus status, PersistenceHealthReason reason)
    {
        if (!Enum.IsDefined(status) || !Enum.IsDefined(reason)
            || (status == PersistenceHealthStatus.Healthy) != (reason == PersistenceHealthReason.Ok))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Status = status;
        Reason = reason;
    }

    public static PersistenceHealthResult Healthy { get; } = new(PersistenceHealthStatus.Healthy, PersistenceHealthReason.Ok);

    public PersistenceHealthStatus Status { get; }

    public PersistenceHealthReason Reason { get; }

    /// <summary>An unhealthy result with <paramref name="reason"/>.</summary>
    public static PersistenceHealthResult Unhealthy(PersistenceHealthReason reason) => new(PersistenceHealthStatus.Unhealthy, reason);
}

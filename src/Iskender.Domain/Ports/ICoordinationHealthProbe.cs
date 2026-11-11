namespace Iskender.Domain.Ports;

/// <summary>
/// Reports whether each distributed coordinator in use answers (FR-025, TR-14). It never throws for a coordinator
/// condition, and returns within <c>healthProbeTimeout</c> plus a fixed 2-second allowance. The caller's token firing
/// throws <c>coordination.cancelled</c>. Liveness never calls it. Owned by ISK-0005.
/// </summary>
public interface ICoordinationHealthProbe
{
    ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken);
}

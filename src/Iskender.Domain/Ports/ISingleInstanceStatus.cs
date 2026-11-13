namespace Iskender.Domain.Ports;

/// <summary>
/// The current single-instance verification state, read by the coordination gate before every call (FR-007a). Owned by
/// ISK-0005.
/// </summary>
public interface ISingleInstanceStatus
{
    SingleInstanceState State { get; }
}

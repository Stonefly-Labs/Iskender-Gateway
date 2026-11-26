namespace Iskender.Infrastructure.Container;

/// <summary>
/// Registered by the entry point when its plane definition maps a traffic listener, so the <c>container</c> settings
/// check the transport fields only for such a plane, and the worker ignores them (research R13). Owned by ISK-0010.
/// </summary>
internal sealed class ContainerTrafficListener;

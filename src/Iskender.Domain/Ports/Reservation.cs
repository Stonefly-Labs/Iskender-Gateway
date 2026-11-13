namespace Iskender.Domain.Ports;

/// <summary>
/// A reservation's result: granted or denied, and the counter's value afterwards (the unchanged value when denied, zero
/// for an absent key). A denial is a result, not an error (FR-012). Owned by ISK-0005.
/// </summary>
public sealed record Reservation(bool Granted, long Value);

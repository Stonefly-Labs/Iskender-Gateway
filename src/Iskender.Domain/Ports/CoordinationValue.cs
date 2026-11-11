namespace Iskender.Domain.Ports;

/// <summary>A counter's value and its version, which every change raises by one. Owned by ISK-0005.</summary>
public sealed record CoordinationValue(long Value, long Version);

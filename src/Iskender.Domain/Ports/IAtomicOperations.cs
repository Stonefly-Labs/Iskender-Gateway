namespace Iskender.Domain.Ports;

/// <summary>
/// Single-key atomic operations on a scoped counter (FR-011 to FR-013, TR-8). Each operation is atomic on its
/// coordinator, checks its scope, name, amounts, and expiry, then the deadline and cancellation, before any I/O, and fails
/// only with a <c>GatewayException</c>. A denied reservation is a result, not an error. Multi-key transactions are not
/// offered. Every change raises the key's version by one. Owned by ISK-0005.
/// </summary>
public interface IAtomicOperations
{
    /// <summary>
    /// Adds a signed <paramref name="delta"/> and returns the new value. Creates the key with <paramref name="expiry"/>
    /// when it is absent; a present key keeps its expiry. A result outside the 64-bit range fails with
    /// <c>core.value.invalid</c> and changes nothing.
    /// </summary>
    ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken);

    /// <summary>
    /// Reserves <paramref name="amount"/> (more than zero) only if <c>value + amount ≤ limit</c> (<paramref name="limit"/>
    /// zero or more), checked and applied atomically. A reservation that would overflow is denied, never wrapped. Creates
    /// the key with <paramref name="expiry"/> when it is absent.
    /// </summary>
    ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken);

    /// <summary>
    /// Subtracts <paramref name="amount"/> (more than zero), never below zero, and returns the new value. An absent key
    /// stays absent and returns <c>null</c>. It is not tied to a reservation and is not idempotent: the caller releases
    /// what it reserved, once.
    /// </summary>
    ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken);

    /// <summary>
    /// Sets <paramref name="value"/> only when the stored version equals <paramref name="expectedVersion"/>, and returns
    /// whether it did. An <paramref name="expectedVersion"/> of zero creates an absent key with <paramref name="expiry"/>.
    /// </summary>
    ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken);

    /// <summary>The value and version, or <c>null</c> when the key is absent or expired.</summary>
    ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken);
}

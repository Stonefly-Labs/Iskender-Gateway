using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The argument checks every atomic operations implementation makes before any I/O (FR-011 to FR-013): amounts, limits,
/// and expiries. The scope, name, deadline, and cancellation checks are <see cref="LeasePolicy"/>'s. Public because the
/// Redis and PostgreSQL implementations use it too. Owned by ISK-0005.
/// </summary>
public static class AtomicPolicy
{
    /// <summary>An amount of zero or less fails with <c>core.value.invalid</c>.</summary>
    public static void RequireAmount(long amount)
    {
        if (amount <= 0)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>A negative limit fails with <c>core.value.invalid</c>.</summary>
    public static void RequireLimit(long limit)
    {
        if (limit < 0)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>An expiry of zero or less, or above <c>keys.maximumExpiry</c>, fails with <c>core.value.invalid</c>.</summary>
    public static void RequireExpiry(CoordinationSettings settings, TimeSpan expiry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (expiry <= TimeSpan.Zero || expiry > settings.MaximumExpiry)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    /// <summary>Whether <c>value + amount ≤ limit</c>, for a positive amount and a non-negative limit, without overflowing.</summary>
    public static bool Fits(long value, long amount, long limit) => value <= limit - amount;

    /// <summary>The value after releasing a positive <paramref name="amount"/>, never below zero and without overflowing.</summary>
    public static long Released(long value, long amount) => value <= amount ? 0 : value - amount;
}

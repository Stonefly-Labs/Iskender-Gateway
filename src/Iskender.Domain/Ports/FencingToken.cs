using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A lease's fencing token: a positive 64-bit integer, strictly increasing per lease key and never reused (FR-014).
/// Ordered, so a store can refuse a stale holder. Owned by ISK-0005.
/// </summary>
public readonly record struct FencingToken : IComparable<FencingToken>
{
    /// <summary>A zero or negative value fails with <c>core.value.invalid</c>.</summary>
    public FencingToken(long value)
    {
        Value = value > 0 ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public long Value { get; }

    public static bool operator <(FencingToken left, FencingToken right) => left.Value < right.Value;

    public static bool operator >(FencingToken left, FencingToken right) => left.Value > right.Value;

    public static bool operator <=(FencingToken left, FencingToken right) => left.Value <= right.Value;

    public static bool operator >=(FencingToken left, FencingToken right) => left.Value >= right.Value;

    public int CompareTo(FencingToken other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A granted lease (FR-014, FR-016, research R12). Its <see cref="Remaining"/> time is the time the coordinator reported
/// at grant less the time elapsed since a monotonic timestamp taken before the request was sent, so network delay only
/// shortens it. <see cref="IsSafeToAct"/> is false at or below the safety margin. <see cref="ToString"/> shows no scope
/// ID, key hash, or holder. Owned by ISK-0005.
/// </summary>
public sealed class Lease
{
    private readonly TimeProvider _timeProvider;
    private readonly long _sentTimestamp;
    private readonly TimeSpan _granted;

    private Lease(CoordinationScope scope, CoordinationName name, FencingToken token, Guid holder, CoordinatorKind issuer, TimeSpan granted, long sentTimestamp, TimeSpan safetyMargin, TimeProvider timeProvider)
    {
        Scope = scope;
        Name = name;
        Token = token;
        Holder = holder;
        Issuer = issuer;
        _granted = granted;
        _sentTimestamp = sentTimestamp;
        SafetyMargin = safetyMargin;
        _timeProvider = timeProvider;
    }

    public CoordinationScope Scope { get; }

    public CoordinationName Name { get; }

    /// <summary>The fencing token, strictly increasing per lease key and never reused.</summary>
    public FencingToken Token { get; }

    /// <summary>A random 128-bit value naming this holder, compared before every renewal and release.</summary>
    public Guid Holder { get; }

    /// <summary>The coordinator that granted the lease. The fencing guard accepts only <see cref="CoordinatorKind.PostgreSql"/>.</summary>
    public CoordinatorKind Issuer { get; }

    /// <summary><c>leases.safetyMargin</c>, below which a holder must stop acting.</summary>
    public TimeSpan SafetyMargin { get; }

    /// <summary>The granted time less the time since the request was sent, never below zero.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var remaining = _granted - _timeProvider.GetElapsedTime(_sentTimestamp);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Whether <see cref="Remaining"/> is above <see cref="SafetyMargin"/>.</summary>
    public bool IsSafeToAct => Remaining > SafetyMargin;

    /// <summary>
    /// For coordinators only: a lease granted by <paramref name="issuer"/>, which reported <paramref name="granted"/>
    /// remaining after a request sent at <paramref name="sentTimestamp"/> on <paramref name="timeProvider"/>'s monotonic
    /// clock. An invalid value fails with <c>core.value.invalid</c>.
    /// </summary>
    public static Lease Grant(
        CoordinationScope scope,
        CoordinationName name,
        FencingToken token,
        Guid holder,
        CoordinatorKind issuer,
        TimeSpan granted,
        long sentTimestamp,
        TimeSpan safetyMargin,
        TimeProvider timeProvider)
    {
        if (scope is null || name is null || token.Value <= 0 || holder == Guid.Empty || !Enum.IsDefined(issuer)
            || granted < TimeSpan.Zero || safetyMargin < TimeSpan.Zero || timeProvider is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(scope, name, token, holder, issuer, granted, sentTimestamp, safetyMargin, timeProvider);
    }

    public override string ToString() => $"lease {Name.Module}/{Name.Name} token {Token.Value} from {Issuer.ToCode()}";
}

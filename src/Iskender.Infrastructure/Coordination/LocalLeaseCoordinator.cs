using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The local substitute for leases and fencing tokens (FR-005, FR-010): exact within this process only, so it declares
/// <c>local</c> for every primitive except fencing, which it does not offer, and is never reported as distributed. Each key has its own lock; tokens come from
/// one process-wide counter, so they increase strictly across every key; expiry and <see cref="Lease.Remaining"/> use
/// the injected <see cref="TimeProvider"/>. Renewal and release compare the holder and token first. A released or expired
/// key holds no memory: its slot is removed when next touched or swept. Owned by ISK-0005.
/// </summary>
public sealed class LocalLeaseCoordinator : ILeaseCoordinator
{
    private static long s_lastToken;

    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly LocalEntries<Slot> _slots;

    public LocalLeaseCoordinator(CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _slots = new LocalEntries<Slot>(_timeProvider);
    }

    /// <summary>What this substitute declares: <c>local</c> for every primitive except fencing, which it does not offer.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Local;

    /// <summary>How many keys hold a slot, so a test can prove released and expired keys are freed.</summary>
    internal int Count => _slots.Count;

    public async ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireDuration(_settings, duration);
        LeasePolicy.RequireWait(_settings, wait);
        var key = CoordinationKey.Hash(scope.Owner, name.Module, name.Name);
        return await Measure(name.Module, "acquire", () => LeasePolicy.AcquireAsync(scope, wait, _timeProvider, _ => ValueTask.FromResult(TryTake(key, scope, name, duration)), cancellationToken));
    }

    public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireDuration(_settings, duration);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        return await Measure(lease.Name.Module, "renew", () =>
        {
            var renewed = _slots.Run(Key(lease), create: false, (slot, sent) =>
            {
                if (slot is null || !slot.Holds(lease) || slot.Expired(sent))
                {
                    return null;
                }

                slot.ExpiresAt = sent + Ticks(duration);
                return Lease.Grant(lease.Scope, lease.Name, lease.Token, lease.Holder, CoordinatorKind.Local, duration, sent, _settings.SafetyMargin, _timeProvider);
            });
            if (renewed is not null)
            {
                return ValueTask.FromResult(renewed);
            }

            CoordinationLog.LogLeaseLost(_logger, lease.Name.Module, CoordinatorKind.Local);
            CoordinationTelemetry.RecordLeaseLost(lease.Name.Module, CoordinatorKind.Local);
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationLeaseLost, CoordinationFailureClass.LeaseLost);
        });
    }

    public async ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        await Measure(lease.Name.Module, "release", () =>
        {
            return ValueTask.FromResult(_slots.Run(Key(lease), create: false, (slot, _) =>
            {
                if (slot is not null && slot.Holds(lease))
                {
                    slot.Holder = Guid.Empty;
                }

                return true;
            }));
        });
    }

    private static string Key(Lease lease) => CoordinationKey.Hash(lease.Scope.Owner, lease.Name.Module, lease.Name.Name);

    // A lease from another coordinator is not this one's to renew or release.
    private static void RequireOwn(Lease lease)
    {
        if (lease is null || lease.Issuer != CoordinatorKind.Local)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    private LeaseAcquisition TryTake(string key, CoordinationScope scope, CoordinationName name, TimeSpan duration) =>
        _slots.Run(key, create: true, (slot, sent) =>
        {
            if (!slot!.IsIdle(sent))
            {
                return LeaseAcquisition.Contended;
            }

            slot.Holder = Guid.NewGuid();
            slot.Token = Interlocked.Increment(ref s_lastToken);
            slot.ExpiresAt = sent + Ticks(duration);
            return LeaseAcquisition.Acquired(Lease.Grant(scope, name, new FencingToken(slot.Token), slot.Holder, CoordinatorKind.Local, duration, sent, _settings.SafetyMargin, _timeProvider));
        });

    private long Ticks(TimeSpan duration) => (long)(duration.TotalSeconds * _timeProvider.TimestampFrequency);

    private ValueTask<T> Measure<T>(string module, string operation, Func<ValueTask<T>> work) =>
        CoordinationTelemetry.MeasureAsync(module, CoordinationPrimitive.Leases, operation, CoordinatorKind.Local, _timeProvider, work);

    private sealed class Slot : LocalEntry
    {
        public Guid Holder { get; set; }

        public long Token { get; set; }

        public long ExpiresAt { get; set; }

        public bool Holds(Lease lease) => Holder == lease.Holder && Token == lease.Token.Value;

        public bool Expired(long now) => now >= ExpiresAt;

        public override bool IsIdle(long now) => Holder == Guid.Empty || Expired(now);
    }
}

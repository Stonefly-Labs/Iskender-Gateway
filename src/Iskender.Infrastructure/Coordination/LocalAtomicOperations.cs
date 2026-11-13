using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The local substitute for atomic operations (FR-005, FR-010): exact within this process only, so it declares
/// <c>local</c> and is never reported as distributed. Each key has its own lock, and expiry uses the injected
/// <see cref="TimeProvider"/>: an expired key counts as absent and is replaced by the next write. A key that is absent or
/// expired holds no memory: a read or release of it adds nothing, and its entry is removed when next touched or swept.
/// Owned by ISK-0005.
/// </summary>
public sealed class LocalAtomicOperations : IAtomicOperations
{
    private readonly CoordinationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly LocalEntries<Entry> _entries;

    public LocalAtomicOperations(CoordinationSettings settings, TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _entries = new LocalEntries<Entry>(_timeProvider);
    }

    /// <summary>What this substitute declares: <c>local</c> for every primitive except fencing, which it does not offer.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Local;

    /// <summary>How many keys hold an entry, so a test can prove expired keys are freed.</summary>
    internal int Count => _entries.Count;

    public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(name.Module, "add", () => Change(scope, name, create: true, entry =>
        {
            var value = entry.Present ? entry.Value : 0;
            if ((delta > 0 && value > long.MaxValue - delta) || (delta < 0 && value < long.MinValue - delta))
            {
                throw new GatewayException(ErrorCodes.ValueInvalid);
            }

            entry.Set(value + delta, expiry, _timeProvider);
            return new CoordinationValue(entry.Value, entry.Version);
        }));
    }

    public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        AtomicPolicy.RequireLimit(limit);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(name.Module, "reserve", () => Change(scope, name, create: true, entry =>
        {
            var value = entry.Present ? entry.Value : 0;
            if (!AtomicPolicy.Fits(value, amount, limit))
            {
                return new Reservation(false, value);
            }

            entry.Set(value + amount, expiry, _timeProvider);
            return new Reservation(true, entry.Value);
        }));
    }

    public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(name.Module, "release", () => Change(scope, name, create: false, entry =>
        {
            if (!entry.Present)
            {
                return null;
            }

            entry.Set(AtomicPolicy.Released(entry.Value, amount), expiry: null, _timeProvider);
            return (CoordinationValue?)new CoordinationValue(entry.Value, entry.Version);
        }));
    }

    public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireExpiry(_settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(name.Module, "compare_and_set", () => Change(scope, name, create: true, entry =>
        {
            if ((entry.Present ? entry.Version : 0) != expectedVersion)
            {
                return false;
            }

            entry.Set(value, expiry, _timeProvider);
            return true;
        }));
    }

    public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return Measure(name.Module, "read", () => Change(scope, name, create: false, entry => entry.Present ? new CoordinationValue(entry.Value, entry.Version) : null));
    }

    // Runs work on the key's entry under its lock, after clearing it if it has expired. Without create, an absent key runs
    // work on a detached, absent entry that is never stored.
    private ValueTask<T> Change<T>(CoordinationScope scope, CoordinationName name, bool create, Func<Entry, T> work) =>
        ValueTask.FromResult(_entries.Run(CoordinationKey.Hash(scope.Owner, name.Module, name.Name), create, (entry, now) =>
        {
            entry ??= new Entry();
            if (entry.Present && now >= entry.ExpiresAt)
            {
                entry.Present = false;
            }

            return work(entry);
        }));

    private ValueTask<T> Measure<T>(string module, string operation, Func<ValueTask<T>> work) =>
        CoordinationTelemetry.MeasureAsync(module, CoordinationPrimitive.AtomicOperations, operation, CoordinatorKind.Local, _timeProvider, work);

    private sealed class Entry : LocalEntry
    {
        public bool Present { get; set; }

        public long Value { get; private set; }

        public long Version { get; private set; }

        public long ExpiresAt { get; private set; }

        public override bool IsIdle(long now) => !Present || now >= ExpiresAt;

        // Creating the key starts its version at 1 and sets its expiry; changing it raises the version and keeps the expiry.
        public void Set(long value, TimeSpan? expiry, TimeProvider timeProvider)
        {
            if (!Present)
            {
                Present = true;
                Version = 0;
                ExpiresAt = timeProvider.GetTimestamp() + (long)(expiry!.Value.TotalSeconds * timeProvider.TimestampFrequency);
            }

            Value = value;
            Version++;
        }
    }
}

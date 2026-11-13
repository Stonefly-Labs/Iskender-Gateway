using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// One feature's view of the coordination ports, from <see cref="CoordinationGate.ForFeature"/>. Each facade admits the
/// call through the gate before it touches the port (FR-009). Owned by ISK-0005.
/// </summary>
public sealed class FeatureCoordination
{
    internal FeatureCoordination(CoordinationGate gate, string name)
    {
        Name = name;
        Atomic = new AtomicFacade(gate, name);
        Leases = new LeaseFacade(gate, name);
        Fencing = new FencingFacade(gate, name);
        Cache = new CacheFacade(gate, name);
    }

    /// <summary>The feature's name, as asked for.</summary>
    public string Name { get; }

    public IAtomicOperations Atomic { get; }

    public ILeaseCoordinator Leases { get; }

    public IFencingGuard Fencing { get; }

    public IVersionedCache Cache { get; }

    private sealed class AtomicFacade(CoordinationGate gate, string feature) : IAtomicOperations
    {
        public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.AtomicOperations);
            return gate.AtomicPort.AddAsync(scope, name, delta, expiry, cancellationToken);
        }

        public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.AtomicOperations);
            return gate.AtomicPort.ReserveAsync(scope, name, amount, limit, expiry, cancellationToken);
        }

        public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.AtomicOperations);
            return gate.AtomicPort.ReleaseAsync(scope, name, amount, cancellationToken);
        }

        public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.AtomicOperations);
            return gate.AtomicPort.CompareAndSetAsync(scope, name, expectedVersion, value, expiry, cancellationToken);
        }

        public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.AtomicOperations);
            return gate.AtomicPort.ReadAsync(scope, name, cancellationToken);
        }
    }

    private sealed class LeaseFacade(CoordinationGate gate, string feature) : ILeaseCoordinator
    {
        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.Leases);
            return gate.LeasePort.AcquireAsync(scope, name, duration, wait, cancellationToken);
        }

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.Leases);
            return gate.LeasePort.RenewAsync(lease, duration, cancellationToken);
        }

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.Leases);
            return gate.LeasePort.ReleaseAsync(lease, cancellationToken);
        }
    }

    private sealed class FencingFacade(CoordinationGate gate, string feature) : IFencingGuard
    {
        public ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.Fencing);
            return gate.FencingPort is { } fencing
                ? fencing.AcceptAsync(unit, resource, lease, cancellationToken)
                : throw CoordinationFailures.Raise(ErrorCodes.CoordinationCapabilityUnsupported, CoordinationFailureClass.CapabilityUnsupported);
        }
    }

    private sealed class CacheFacade(CoordinationGate gate, string feature) : IVersionedCache
    {
        public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.CacheInvalidation);
            return gate.CachePort.TryGetAsync(scope, cacheNamespace, name, expectedVersion, cancellationToken);
        }

        public ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.CacheInvalidation);
            return gate.CachePort.SetAsync(scope, cacheNamespace, name, version, value, expiry, stamp, cancellationToken);
        }

        public ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken)
        {
            gate.Admit(feature, CoordinationPrimitive.CacheInvalidation);
            return gate.CachePort.InvalidateAsync(scope, cacheNamespace, cancellationToken);
        }
    }
}

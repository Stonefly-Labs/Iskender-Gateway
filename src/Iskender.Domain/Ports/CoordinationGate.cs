using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The only route from a feature to the coordination ports (FR-009, research R5). It is built from the capability
/// report, the configured ports, and the single-instance status, and hands each feature a <see cref="FeatureCoordination"/>
/// whose facades check, before every call, that the feature was accepted, that it declared the primitive, and, for a
/// <c>single_instance</c> feature, that this process still holds the instance lease. A refused or unknown feature never
/// reaches a port, and never falls back to a local substitute (contract Ownership rule 3). Owned by ISK-0005.
/// </summary>
public sealed class CoordinationGate
{
    /// <summary>
    /// <paramref name="fencing"/> is <c>null</c> when PostgreSQL is not configured, and every fencing call then fails with
    /// <c>coordination.capability_unsupported</c>. Any other <c>null</c> argument fails with <c>core.value.invalid</c>.
    /// </summary>
    public CoordinationGate(
        CapabilityReport report,
        IAtomicOperations atomic,
        ILeaseCoordinator leases,
        IVersionedCache cache,
        IFencingGuard? fencing,
        ISingleInstanceStatus singleInstance)
    {
        if (report is null || atomic is null || leases is null || cache is null || singleInstance is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Report = report;
        AtomicPort = atomic;
        LeasePort = leases;
        CachePort = cache;
        FencingPort = fencing;
        SingleInstance = singleInstance;
    }

    public CapabilityReport Report { get; }

    internal IAtomicOperations AtomicPort { get; }

    internal ILeaseCoordinator LeasePort { get; }

    internal IVersionedCache CachePort { get; }

    internal IFencingGuard? FencingPort { get; }

    internal ISingleInstanceStatus SingleInstance { get; }

    /// <summary>
    /// The coordination for the feature named <paramref name="name"/>. An unknown or refused feature still gets one, and
    /// every call through it fails with <c>coordination.capability_unsupported</c>.
    /// </summary>
    public FeatureCoordination ForFeature(string name) => new(this, name);

    /// <summary>
    /// Fails with <c>coordination.capability_unsupported</c> unless the feature was accepted, declared
    /// <paramref name="primitive"/>, and, when accepted as <c>single_instance</c>, this process has not since lost the
    /// instance lease. The state is read on every call, so only calls after a loss fail (contract Requirements rule 6).
    /// </summary>
    internal void Admit(string name, CoordinationPrimitive primitive)
    {
        var capability = Report.Find(name);
        if (capability is not { Accepted: true } || !capability.Feature.Uses(primitive)
            || (capability.Mode == CoordinationMode.SingleInstance && SingleInstance.State is SingleInstanceState.Lost or SingleInstanceState.HeldByOther))
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationCapabilityUnsupported, CoordinationFailureClass.CapabilityUnsupported);
        }
    }
}

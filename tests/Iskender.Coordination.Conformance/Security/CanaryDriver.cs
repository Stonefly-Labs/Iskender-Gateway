using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Security;

/// <summary>
/// What every canary suite drives (SC-005, User Story 5 scenario 2): every port operation in an organization whose ID holds
/// the canary, with the canary as a cached value, and a capability refusal through the gate. Each result and failure is
/// reported to <see cref="CapturingDiagnostics.Shared"/>, whose <see cref="CapturingDiagnostics.CanaryHits"/> the suite
/// then checks.
/// </summary>
public static class CanaryDriver
{
    /// <summary>The module every canary operation uses.</summary>
    public const string Module = "canary";

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    /// <summary>Scopes whose organization A has <see cref="Canary.OrganizationId"/>.</summary>
    public static ConformanceScopes Scopes() => new(Canary.OrganizationId, ResourceId.New(ResourceTypes.Organization));

    private static CapturingDiagnostics Diagnostics => CapturingDiagnostics.Shared;

    /// <summary>
    /// Runs each atomic operation, a lease's acquire, renew, and release, and the cache's miss, set, hit, and bump, in
    /// <paramref name="scope"/>, reporting every result.
    /// </summary>
    public static async Task DriveAsync(CoordinatorClient client, CoordinationScope scope, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var counter = new CoordinationName(Module, "counter");
        Diagnostics.Report(await client.Atomic.AddAsync(scope, counter, 1, Expiry, cancellationToken));
        Diagnostics.Report(await client.Atomic.ReserveAsync(scope, counter, 1, 10, Expiry, cancellationToken));
        Diagnostics.Report(await client.Atomic.ReserveAsync(scope, counter, 100, 10, Expiry, cancellationToken));
        Diagnostics.Report(await client.Atomic.ReleaseAsync(scope, counter, 1, cancellationToken));
        var read = await client.Atomic.ReadAsync(scope, counter, cancellationToken);
        Diagnostics.Report(read);
        Diagnostics.Report(await client.Atomic.CompareAndSetAsync(scope, counter, read!.Version, 5, Expiry, cancellationToken));
        Diagnostics.Report(await client.Atomic.CompareAndSetAsync(scope, counter, read.Version, 6, Expiry, cancellationToken));

        var resource = new CoordinationName(Module, "resource");
        var acquisition = await client.Leases.AcquireAsync(scope, resource, leaseDuration, LeaseWait.TryOnly, cancellationToken);
        Diagnostics.Report(acquisition);
        var lease = acquisition.Lease ?? throw new InvalidOperationException("The canary lease was not acquired.");
        Diagnostics.Report(lease);
        Diagnostics.Report(await client.Leases.AcquireAsync(scope, resource, leaseDuration, LeaseWait.TryOnly, cancellationToken));
        var renewed = await client.Leases.RenewAsync(lease, leaseDuration, cancellationToken);
        Diagnostics.Report(renewed);
        await client.Leases.ReleaseAsync(renewed, cancellationToken);

        var cacheNamespace = new CacheNamespace(Module, "entries");
        var miss = await client.Cache.TryGetAsync(scope, cacheNamespace, "entry", 1, cancellationToken);
        Diagnostics.Report(miss);
        Diagnostics.Report(await client.Cache.SetAsync(scope, cacheNamespace, "entry", 1, Canary.Bytes, Expiry, miss.Stamp, cancellationToken));
        var hit = await client.Cache.TryGetAsync(scope, cacheNamespace, "entry", 1, cancellationToken);
        Diagnostics.Report(hit);
        if (!hit.IsHit || !hit.Value.Span.SequenceEqual(Canary.Bytes))
        {
            throw new InvalidOperationException("The canary value was not cached.");
        }

        await client.Cache.InvalidateAsync(scope, cacheNamespace, cancellationToken);
        Diagnostics.Report(await client.Cache.TryGetAsync(scope, cacheNamespace, "entry", 1, cancellationToken));
    }

    /// <summary>
    /// A capability refusal: a <c>strict</c> counter in <c>multi_replica</c> where atomic operations are only
    /// <c>leased</c>, called through a gate over <paramref name="client"/>'s ports in <paramref name="scope"/>. The call
    /// never reaches a port. Returns the failure, already reported.
    /// </summary>
    public static async Task<GatewayException> RefuseAsync(CoordinatorClient client, CoordinationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var feature = new FeatureRequirement(Module + ".refused_counter", Module, new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict }, mandatoryPath: false, OutageBehavior.FailClosed);
        var report = CoordinationCapabilityChecker.Check([feature], CoordinationTopology.MultiReplica, Enum.GetValues<CoordinationPrimitive>().ToDictionary(primitive => primitive, _ => (CoordinatorCapabilities?)CoordinatorCapabilities.Redis), SingleInstanceState.NotApplicable);
        Diagnostics.Report(report);
        var gate = new CoordinationGate(report, client.Atomic, client.Leases, client.Cache, fencing: null, new NotApplicable());
        try
        {
            await gate.ForFeature(feature.Name).Atomic.AddAsync(scope, new CoordinationName(Module, "refused"), 1, Expiry, cancellationToken);
        }
        catch (GatewayException failure) when (ReferenceEquals(failure.Code, ErrorCodes.CoordinationCapabilityUnsupported))
        {
            Diagnostics.Report(failure);
            return failure;
        }

        throw new InvalidOperationException("The refused feature reached the port.");
    }

    /// <summary>Runs <paramref name="call"/>, which must fail, and reports the failure.</summary>
    public static async Task<GatewayException> FailAsync(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            await call();
        }
        catch (GatewayException failure)
        {
            Diagnostics.Report(failure);
            return failure;
        }

        throw new InvalidOperationException("The call was expected to fail.");
    }

    private sealed class NotApplicable : ISingleInstanceStatus
    {
        public SingleInstanceState State => SingleInstanceState.NotApplicable;
    }
}

using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using static Iskender.Domain.Tests.Ports.CapabilityFixtures;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationGateTests
{
    private static readonly CoordinationName Resource = new("fixture", "resource");

    private static readonly CacheNamespace Namespace = new("fixture", "entries");

    private static readonly FeatureRequirement Everything = new(
        "fixture.everything",
        "fixture",
        Enum.GetValues<CoordinationPrimitive>().ToDictionary(primitive => primitive, _ => ConsistencyLevel.BestEffort),
        mandatoryPath: false,
        OutageBehavior.FailClosed);

    /// <summary>Every facade method, with the primitive it belongs to.</summary>
    private static readonly (string Method, CoordinationPrimitive Primitive, Func<FeatureCoordination, ValueTask> Call)[] Calls =
    [
        ("Atomic.AddAsync", CoordinationPrimitive.AtomicOperations, async f => await f.Atomic.AddAsync(CoordinationScope.Operator, Resource, 1, TimeSpan.FromMinutes(1), default)),
        ("Atomic.ReserveAsync", CoordinationPrimitive.AtomicOperations, async f => await f.Atomic.ReserveAsync(CoordinationScope.Operator, Resource, 1, 10, TimeSpan.FromMinutes(1), default)),
        ("Atomic.ReleaseAsync", CoordinationPrimitive.AtomicOperations, async f => await f.Atomic.ReleaseAsync(CoordinationScope.Operator, Resource, 1, default)),
        ("Atomic.CompareAndSetAsync", CoordinationPrimitive.AtomicOperations, async f => await f.Atomic.CompareAndSetAsync(CoordinationScope.Operator, Resource, 0, 1, TimeSpan.FromMinutes(1), default)),
        ("Atomic.ReadAsync", CoordinationPrimitive.AtomicOperations, async f => await f.Atomic.ReadAsync(CoordinationScope.Operator, Resource, default)),
        ("Leases.AcquireAsync", CoordinationPrimitive.Leases, async f => await f.Leases.AcquireAsync(CoordinationScope.Operator, Resource, TimeSpan.FromSeconds(5), LeaseWait.TryOnly, default)),
        ("Leases.RenewAsync", CoordinationPrimitive.Leases, async f => await f.Leases.RenewAsync(null!, TimeSpan.FromSeconds(5), default)),
        ("Leases.ReleaseAsync", CoordinationPrimitive.Leases, f => f.Leases.ReleaseAsync(null!, default)),
        ("Fencing.AcceptAsync", CoordinationPrimitive.Fencing, f => f.Fencing.AcceptAsync(null!, Resource, null!, default)),
        ("Cache.TryGetAsync", CoordinationPrimitive.CacheInvalidation, async f => await f.Cache.TryGetAsync(CoordinationScope.Operator, Namespace, "entry", 1, default)),
        ("Cache.SetAsync", CoordinationPrimitive.CacheInvalidation, async f => await f.Cache.SetAsync(CoordinationScope.Operator, Namespace, "entry", 1, new byte[] { 1 }, TimeSpan.FromMinutes(1), null, default)),
        ("Cache.InvalidateAsync", CoordinationPrimitive.CacheInvalidation, f => f.Cache.InvalidateAsync(CoordinationScope.Operator, Namespace, default)),
    ];

    public static TheoryData<string> Methods() => [.. Calls.Select(call => call.Method)];

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ARefusedFeatureFailsEveryCallAndReachesNoPort(string method)
    {
        var (gate, ports, _) = Build(CoordinationTopology.MultiReplica, Everything, HardBudget);

        await AssertRefused(Call(method), gate.ForFeature(HardBudget.Name));

        Assert.Equal(0, ports.Calls);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task AnUnknownFeatureFailsEveryCallAndReachesNoPort(string method)
    {
        var (gate, ports, _) = Build(CoordinationTopology.MultiReplica, Everything);

        await AssertRefused(Call(method), gate.ForFeature("fixture.unknown"));
        await AssertRefused(Call(method), gate.ForFeature(null!));

        Assert.Equal(0, ports.Calls);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task AnUndeclaredPrimitiveFailsAndReachesNoPort(string method)
    {
        var (gate, ports, _) = Build(CoordinationTopology.MultiReplica, StrictCounter, LeasedLease, BestEffortCache);
        var (_, primitive, call) = Calls.Single(entry => entry.Method == method);

        foreach (var feature in new[] { StrictCounter, LeasedLease, BestEffortCache }.Where(feature => !feature.Uses(primitive)))
        {
            await AssertRefused(call, gate.ForFeature(feature.Name));
        }

        Assert.Equal(0, ports.Calls);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task AnAcceptedFeatureReachesThePortOnce(string method)
    {
        var (gate, ports, _) = Build(CoordinationTopology.MultiReplica, Everything);

        await Call(method)(gate.ForFeature(Everything.Name));

        Assert.Equal(1, ports.Calls);
        Assert.Equal(method, Assert.Single(ports.Methods));
    }

    [Theory]
    [InlineData(SingleInstanceState.Lost)]
    [InlineData(SingleInstanceState.HeldByOther)]
    public async Task ALostInstanceLeaseFailsSingleInstanceFeaturesUntilItIsHeldAgain(SingleInstanceState lost)
    {
        var (gate, ports, status) = Build(CoordinationTopology.SingleInstance, Everything);
        var feature = gate.ForFeature(Everything.Name);
        Assert.Equal(CoordinationMode.SingleInstance, gate.Report.Find(Everything.Name)!.Mode);

        await feature.Atomic.ReadAsync(CoordinationScope.Operator, Resource, TestContext.Current.CancellationToken);
        status.State = lost;
        foreach (var (_, _, call) in Calls)
        {
            await AssertRefused(call, feature);
        }

        Assert.Equal(1, ports.Calls);
        status.State = SingleInstanceState.Held;
        await feature.Atomic.ReadAsync(CoordinationScope.Operator, Resource, TestContext.Current.CancellationToken);
        Assert.Equal(2, ports.Calls);
    }

    [Fact]
    public async Task ALostInstanceLeaseDoesNotFailDistributedFeatures()
    {
        var (gate, ports, status) = Build(CoordinationTopology.MultiReplica, Everything);
        Assert.Equal(CoordinationMode.Distributed, gate.Report.Find(Everything.Name)!.Mode);

        status.State = SingleInstanceState.Lost;
        foreach (var (_, _, call) in Calls)
        {
            await call(gate.ForFeature(Everything.Name));
        }

        Assert.Equal(Calls.Length, ports.Calls);
    }

    [Fact]
    public async Task WithoutAFencingGuardAnAcceptedFencingCallFails()
    {
        var (gate, ports, _) = Build(CoordinationTopology.MultiReplica, [Everything], fencing: false);

        await AssertRefused(Call("Fencing.AcceptAsync"), gate.ForFeature(Everything.Name));

        Assert.Equal(0, ports.Calls);
    }

    [Fact]
    public void ANullArgumentFailsWithValueInvalid()
    {
        var ports = new CountingPorts();
        var report = CoordinationCapabilityChecker.Check([Everything], CoordinationTopology.MultiReplica, Uniform(CoordinatorCapabilities.PostgreSql), SingleInstanceState.NotApplicable);
        var status = new MutableStatus();

        foreach (var build in new Func<CoordinationGate>[]
        {
            () => new(null!, ports, ports, ports, ports, status),
            () => new(report, null!, ports, ports, ports, status),
            () => new(report, ports, null!, ports, ports, status),
            () => new(report, ports, ports, null!, ports, status),
            () => new(report, ports, ports, ports, ports, null!),
        })
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(build).Code);
        }

        Assert.Same(report, new CoordinationGate(report, ports, ports, ports, null, status).Report);
    }

    private static Func<FeatureCoordination, ValueTask> Call(string method) => Calls.Single(entry => entry.Method == method).Call;

    private static async Task AssertRefused(Func<FeatureCoordination, ValueTask> call, FeatureCoordination feature)
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await call(feature));
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal(CoordinationFailureClass.CapabilityUnsupported, CoordinationFailures.ClassOf(failure));
    }

    private static (CoordinationGate Gate, CountingPorts Ports, MutableStatus Status) Build(CoordinationTopology topology, params FeatureRequirement[] features) =>
        Build(topology, features, fencing: true);

    private static (CoordinationGate Gate, CountingPorts Ports, MutableStatus Status) Build(CoordinationTopology topology, FeatureRequirement[] features, bool fencing)
    {
        var state = topology == CoordinationTopology.SingleInstance ? SingleInstanceState.Held : SingleInstanceState.NotApplicable;
        var report = CoordinationCapabilityChecker.Check(features, topology, Uniform(CoordinatorCapabilities.PostgreSql), state);
        var ports = new CountingPorts();
        var status = new MutableStatus { State = state };
        return (new CoordinationGate(report, ports, ports, ports, fencing ? ports : null, status), ports, status);
    }

    private sealed class MutableStatus : ISingleInstanceStatus
    {
        public SingleInstanceState State { get; set; }
    }

    /// <summary>All four ports, recording each call and returning a default result.</summary>
    private sealed class CountingPorts : IAtomicOperations, ILeaseCoordinator, IVersionedCache, IFencingGuard
    {
        private readonly List<string> _methods = [];

        public int Calls => _methods.Count;

        public IReadOnlyList<string> Methods => _methods;

        public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken) => Record<CoordinationValue>("Atomic.AddAsync");

        public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken) => Record<Reservation>("Atomic.ReserveAsync");

        public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken) => Record<CoordinationValue?>("Atomic.ReleaseAsync");

        public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken) => Record<bool>("Atomic.CompareAndSetAsync");

        public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken) => Record<CoordinationValue?>("Atomic.ReadAsync");

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken) => Record<LeaseAcquisition>("Leases.AcquireAsync");

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) => Record<Lease>("Leases.RenewAsync");

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
        {
            _methods.Add("Leases.ReleaseAsync");
            return ValueTask.CompletedTask;
        }

        public ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken cancellationToken)
        {
            _methods.Add("Fencing.AcceptAsync");
            return ValueTask.CompletedTask;
        }

        public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken) => Record<CacheLookup>("Cache.TryGetAsync");

        public ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken) => Record<bool>("Cache.SetAsync");

        public ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken)
        {
            _methods.Add("Cache.InvalidateAsync");
            return ValueTask.CompletedTask;
        }

        private ValueTask<T> Record<T>(string method)
        {
            _methods.Add(method);
            return ValueTask.FromResult(default(T)!);
        }
    }
}

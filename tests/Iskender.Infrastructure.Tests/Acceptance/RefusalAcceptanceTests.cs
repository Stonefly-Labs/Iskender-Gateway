using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Acceptance;

/// <summary>
/// User Story 1, end to end from a <c>coordination</c> section through the settings, the capability map, the checker, and
/// the gate (half of FR-030's acceptance evidence; the single-instance half is in the PostgreSQL acceptance group).
/// </summary>
public class RefusalAcceptanceTests
{
    private const string Redis = "\"redis\":{\"endpoints\":[\"cache-1.internal:6379\"],\"username\":\"isk_coordination\",\"password\":{\"secretRef\":\"redis/coordination\"}}";

    private static readonly FeatureRequirement StrictCounter = Feature("fixture.strict_counter", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict);

    private static readonly FeatureRequirement LeasedLease = Feature("fixture.leased_lease", CoordinationPrimitive.Leases, ConsistencyLevel.Leased);

    private static readonly FeatureRequirement BestEffortCache = Feature("fixture.best_effort_cache", CoordinationPrimitive.CacheInvalidation, ConsistencyLevel.BestEffort);

    private static readonly FeatureRequirement HardBudget = Feature("fixture.hard_budget", CoordinationPrimitive.AtomicOperations, ConsistencyLevel.Strict, mandatoryPath: true, OutageBehavior.Degrade("fixture.hard_budget_degraded"));

    private static readonly CoordinationName Resource = new("fixture", "resource");

    [Fact]
    public async Task Scenario1MultiReplicaWithOnlyLocalSubstitutesRefusesALeasedLeaseAndItsCallsReachNoState()
    {
        var run = Run("\"topology\":\"multi_replica\"", postgreSqlConfigured: false, LeasedLease);

        Assert.Equal("refused:local_in_multi_replica", Describe(run.Report.Find(LeasedLease.Name)!));
        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await run.Gate.ForFeature(LeasedLease.Name).Leases.AcquireAsync(CoordinationScope.Operator, Resource, TimeSpan.FromSeconds(5), LeaseWait.TryOnly, TestContext.Current.CancellationToken));
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal(0, run.Ports.Calls);
        Assert.Contains(run.Logger.Events, logged => logged.Id.Id == 5002 && Field(logged, "Reason") == "local_in_multi_replica");
    }

    [Fact]
    public void Scenario2NoTopologyInConfigurationGivesTheSameResultAsScenario1()
    {
        var absent = Run("", postgreSqlConfigured: false, LeasedLease);
        var explicitly = Run("\"topology\":\"multi_replica\"", postgreSqlConfigured: false, LeasedLease);

        Assert.Equal(CoordinationTopology.MultiReplica, absent.Report.Topology);
        Assert.Equal(Describe(explicitly.Report.Find(LeasedLease.Name)!), Describe(absent.Report.Find(LeasedLease.Name)!));
    }

    [Fact]
    public void Scenario3SingleInstanceWithLocalSubstitutesAcceptsBestEffortAndLeasedRefusesStrictAndIsUnverified()
    {
        var run = Run("\"topology\":\"single_instance\"", postgreSqlConfigured: false, BestEffortCache, LeasedLease, StrictCounter);

        Assert.Equal("accepted:single_instance", Describe(run.Report.Find(BestEffortCache.Name)!));
        Assert.Equal("accepted:single_instance", Describe(run.Report.Find(LeasedLease.Name)!));
        Assert.Equal("refused:unsupported_consistency", Describe(run.Report.Find(StrictCounter.Name)!));
        Assert.Equal(SingleInstanceState.Unverified, run.Report.SingleInstance);
        Assert.All(run.Logger.Events.Where(logged => logged.Id.Id == 5001), logged => Assert.Equal("single_instance", Field(logged, "Outcome")));
    }

    [Fact]
    public void Scenario4RedisDeclaresLeasedAtomicOperationsSoAStrictCounterIsRefused()
    {
        var run = Run("\"atomic\":{\"coordinator\":\"redis\"}," + Redis, postgreSqlConfigured: false, StrictCounter);

        Assert.Equal("refused:unsupported_consistency", Describe(run.Report.Find(StrictCounter.Name)!));
    }

    [Fact]
    public void Scenario5AHardBudgetPathThatDegradesIsRefused()
    {
        var run = Run(EveryPrimitiveOn("postgresql"), postgreSqlConfigured: true, HardBudget);

        Assert.Equal("refused:degrade_not_permitted", Describe(run.Report.Find(HardBudget.Name)!));
        Assert.Contains(run.Logger.Events, logged => logged.Id.Id == 5002 && Field(logged, "Reason") == "degrade_not_permitted");
    }

    [Fact]
    public async Task Scenario6PostgreSqlForEveryPrimitiveAcceptsEveryFixtureFeatureWithOneEventEach()
    {
        // fixture.hard_budget is refused by rule 1 whatever the coordinator (scenario 5), so it is not among them.
        var run = Run(EveryPrimitiveOn("postgresql"), postgreSqlConfigured: true, StrictCounter, LeasedLease, BestEffortCache);

        Assert.All(run.Report.Features, capability => Assert.Equal("accepted:distributed", Describe(capability)));
        var events = run.Logger.Events.Where(logged => logged.Id.Id is 5001 or 5002).ToList();
        Assert.Equal([StrictCounter.Name, LeasedLease.Name, BestEffortCache.Name], events.Select(logged => Field(logged, "Feature")));
        Assert.All(events, logged =>
        {
            Assert.Equal(5001, logged.Id.Id);
            Assert.Equal("distributed", Field(logged, "Outcome"));
            Assert.EndsWith("=strict", Field(logged, "ConsistencyLevel"), StringComparison.Ordinal);
        });

        await run.Gate.ForFeature(StrictCounter.Name).Atomic.ReadAsync(CoordinationScope.Operator, Resource, TestContext.Current.CancellationToken);
        Assert.Equal(1, run.Ports.Calls);
    }

    [Fact]
    public void ARedisPrimitiveWithNoEndpointsIsRefusedAsNotConfigured()
    {
        var run = Run("\"leases\":{\"coordinator\":\"redis\"}", postgreSqlConfigured: true, LeasedLease);

        Assert.Equal("refused:coordinator_not_configured", Describe(run.Report.Find(LeasedLease.Name)!));
    }

    [Fact]
    public void APostgreSqlPrimitiveWithoutThePersistenceSectionIsRefusedAsNotConfigured()
    {
        var run = Run("\"leases\":{\"coordinator\":\"postgresql\"}", postgreSqlConfigured: false, LeasedLease);

        Assert.Equal("refused:coordinator_not_configured", Describe(run.Report.Find(LeasedLease.Name)!));
    }

    [Fact]
    public void AFeatureNeedingLeasedFencingIsRefusedOnRedisAndAcceptedOnPostgreSql()
    {
        var fenced = Feature("fixture.fenced_write", CoordinationPrimitive.Fencing, ConsistencyLevel.Leased);

        // Fencing follows leases.coordinator (T010).
        var onRedis = Run("\"leases\":{\"coordinator\":\"redis\"}," + Redis, postgreSqlConfigured: true, fenced);
        var onPostgreSql = Run("\"leases\":{\"coordinator\":\"postgresql\"}", postgreSqlConfigured: true, fenced);

        Assert.Equal("refused:unsupported_consistency", Describe(onRedis.Report.Find(fenced.Name)!));
        Assert.Equal("accepted:distributed", Describe(onPostgreSql.Report.Find(fenced.Name)!));
    }

    private static string EveryPrimitiveOn(string coordinator) =>
        $"\"atomic\":{{\"coordinator\":\"{coordinator}\"}},\"leases\":{{\"coordinator\":\"{coordinator}\"}},\"cache\":{{\"coordinator\":\"{coordinator}\"}}";

    // The path a host takes: validate the section, read the settings, map each primitive to its coordinator's declared
    // capabilities (or null when that coordinator has no settings), run the checker, log the report, and build the gate.
    private static (CapabilityReport Report, CoordinationGate Gate, CountingPorts Ports, CapturingDiagnostics.Logger Logger) Run(string fields, bool postgreSqlConfigured, params FeatureRequirement[] features)
    {
        var document = "{\"iskender\":{\"coordination\":{" + fields + "}}}";
        var validation = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, document),
            environmentName: "production"));
        Assert.True(validation.IsValid, validation.ToString());
        var settings = CoordinationSettings.From(validation.Snapshot!.GetSection(CoordinationSectionSchema.Schema));

        var capabilities = CoordinationPrimitives.All.ToDictionary(
            primitive => primitive,
            primitive => settings.IsConfigured(primitive, postgreSqlConfigured)
                ? settings.CoordinatorFor(primitive) switch
                {
                    CoordinatorKind.Redis => CoordinatorCapabilities.Redis,
                    CoordinatorKind.PostgreSql => CoordinatorCapabilities.PostgreSql,
                    _ => CoordinatorCapabilities.Local,
                }
                : null);
        var state = settings.Topology == CoordinationTopology.SingleInstance
            ? (postgreSqlConfigured ? SingleInstanceState.Held : SingleInstanceState.Unverified)
            : SingleInstanceState.NotApplicable;
        var report = CoordinationCapabilityChecker.Check(features, settings.Topology, capabilities, state);

        var logger = new CapturingDiagnostics.Logger();
        CoordinationLog.CapabilityReport(logger, report);
        var ports = new CountingPorts();
        return (report, new CoordinationGate(report, ports, ports, ports, ports, new FixedStatus(state)), ports, logger);
    }

    private static FeatureRequirement Feature(string name, CoordinationPrimitive primitive, ConsistencyLevel minimum, bool mandatoryPath = false, OutageBehavior? outage = null) =>
        new(name, "fixture", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [primitive] = minimum }, mandatoryPath, outage ?? OutageBehavior.FailClosed);

    private static string Describe(FeatureCapability capability) =>
        capability.Accepted ? "accepted:" + capability.Mode!.Value.ToCode() : "refused:" + capability.Reason.ToCode();

    private static string? Field(CapturedLog logged, string name) => logged.Fields.Single(field => field.Key == name).Value as string;

    private sealed class FixedStatus(SingleInstanceState state) : ISingleInstanceStatus
    {
        public SingleInstanceState State => state;
    }

    /// <summary>All four ports, counting every call that reaches them.</summary>
    private sealed class CountingPorts : IAtomicOperations, ILeaseCoordinator, IVersionedCache, IFencingGuard
    {
        private int _calls;

        public int Calls => _calls;

        public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken) => Count<CoordinationValue>();

        public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken) => Count<Reservation>();

        public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken) => Count<CoordinationValue?>();

        public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken) => Count<bool>();

        public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken) => Count<CoordinationValue?>();

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken) => Count<LeaseAcquisition>();

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) => Count<Lease>();

        public async ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken) => await Count<bool>();

        public async ValueTask AcceptAsync(IUnitOfWork unit, CoordinationName resource, Lease lease, CancellationToken cancellationToken) => await Count<bool>();

        public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken) => Count<CacheLookup>();

        public ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken) => Count<bool>();

        public async ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken) => await Count<bool>();

        private ValueTask<T> Count<T>()
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(default(T)!);
        }
    }
}

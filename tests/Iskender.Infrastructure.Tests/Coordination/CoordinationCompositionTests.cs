using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Tests.Coordination;

/// <summary>
/// <see cref="CoordinationStartup"/>'s six steps (research R10) over a validated snapshot and fake
/// <see cref="CoordinatorRegistration"/>s, without a container: the services are a dictionary.
/// </summary>
public sealed class CoordinationCompositionTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();

    private readonly Loggers _loggers = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _loggers.Dispose();

    [Fact]
    public async Task AllLocalComposesTheSubstitutes()
    {
        var (startup, composed, services) = Compose("");

        await startup.StartAsync(services.Snapshot, Token);

        Assert.Same(services.Get<LocalAtomicOperations>(), composed.Atomic);
        Assert.Same(services.Get<LocalLeaseCoordinator>(), composed.Leases);
        Assert.Same(services.Get<LocalVersionedCache>(), composed.Cache);
        Assert.Same(composed.Report, composed.Gate.Report);
        Assert.Equal(CoordinationTopology.MultiReplica, composed.Report.Topology);
        Assert.Equal(SingleInstanceState.NotApplicable, composed.Report.SingleInstance);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, Assert.Throws<GatewayException>(() => composed.Fencing).Code);
    }

    [Fact]
    public async Task ASelectedRedisCoordinatorWithNoRegistrationFailsAndRunsNothingBelow()
    {
        var feature = new FeatureRequirement("rate.count", "coordination", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased }, mandatoryPath: false, OutageBehavior.FailClosed);
        var (startup, composed, services) = Compose("\"atomic\":{\"coordinator\":\"redis\"},\"redis\":{\"endpoints\":[\"localhost:6379\"]}", requirements: [feature]);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => startup.StartAsync(services.Snapshot, Token).AsTask());

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("missing", "redis coordinator for atomic_operations"), failure.Error!.Diagnostics!.Values);
        Assert.Contains(new KeyValuePair<string, string>("requiredBy", "configuration"), failure.Error!.Diagnostics!.Values);
        var missing = Assert.Single(_loggers.Hosting.Events);
        Assert.Equal(6003, missing.Id.Id);
        Assert.Equal("Iskender registration missing: redis coordinator for atomic_operations required by configuration.", missing.Message);
        Assert.Empty(_loggers.Coordination.Events);
        Assert.False(composed.IsComposed);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, Assert.Throws<GatewayException>(() => composed.Guard).Code);
    }

    [Fact]
    public async Task ARegisteredCoordinatorIsSelectedForItsPrimitiveOnly()
    {
        var atomic = new FakeAtomic();
        var redis = new CoordinatorRegistration(CoordinatorKind.Redis, CoordinatorCapabilities.Redis, _ => new FakeHealth(), atomic: _ => atomic, leases: _ => throw new InvalidOperationException("not selected"), cache: _ => throw new InvalidOperationException("not selected"));
        var feature = new FeatureRequirement("rate.count", "coordination", new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased }, mandatoryPath: false, OutageBehavior.FailClosed);
        var (startup, composed, services) = Compose("\"atomic\":{\"coordinator\":\"redis\"},\"redis\":{\"endpoints\":[\"localhost:6379\"]}", [redis], [feature]);

        await startup.StartAsync(services.Snapshot, Token);

        Assert.Same(atomic, composed.Atomic);
        Assert.Same(services.Get<LocalLeaseCoordinator>(), composed.Leases);
        var accepted = Assert.Single(composed.Report.Features);
        Assert.True(accepted.Accepted);
        Assert.Equal(5001, Assert.Single(_loggers.Coordination.Events).Id.Id);
    }

    [Fact]
    public async Task TheGateAndReportFailClosedBeforeStart()
    {
        var (_, composed, _) = Compose("");

        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, Assert.Throws<GatewayException>(() => composed.Gate).Code);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, Assert.Throws<GatewayException>(() => composed.Report).Code);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, Assert.Throws<GatewayException>(() => composed.Atomic).Code);
        await composed.DisposeAsync();
    }

    [Fact]
    public async Task StopReleasesTheGuardsLease()
    {
        var settings = LocalCoordinatorUnderTest.ReadSettings();
        var leases = new PostgreSqlIssuedLeases(new LocalLeaseCoordinator(settings, timeProvider: _clock), _clock);
        var postgreSql = new CoordinatorRegistration(CoordinatorKind.PostgreSql, CoordinatorCapabilities.PostgreSql, _ => new FakeHealth(), atomic: _ => new FakeAtomic(), leases: _ => leases, cache: _ => throw new InvalidOperationException("not selected"));
        var (startup, composed, services) = Compose("\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}", [postgreSql]);

        await startup.StartAsync(services.Snapshot, Token);
        Assert.Equal(SingleInstanceState.Held, composed.Guard.State);
        Assert.Equal(SingleInstanceState.Held, composed.Report.SingleInstance);
        Assert.Equal(0, leases.Releases);

        await startup.StopAsync(Token);

        Assert.Equal(1, leases.Releases);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, leases.Acquisitions);
        await composed.DisposeAsync();
        await composed.DisposeAsync();
        Assert.Equal(1, leases.Releases);
    }

    [Fact]
    public void ACoordinatorRegistrationRejectsLocalAndMismatchedCapabilities()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorRegistration(CoordinatorKind.Local, CoordinatorCapabilities.Local, _ => new FakeHealth())).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorRegistration(CoordinatorKind.Redis, CoordinatorCapabilities.PostgreSql, _ => new FakeHealth())).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinatorRegistration(CoordinatorKind.Redis, CoordinatorCapabilities.Redis, null!)).Code);
    }

    [Theory]
    [InlineData(CompositionPlane.ControlPlane, "control-plane", "instance.control_plane")]
    [InlineData(CompositionPlane.DataPlane, "hosting", "instance.data_plane")]
    [InlineData(null, null, "instance.data_plane")]
    public async Task TheGuardHoldsTheLeaseOfThePlaneTheRootNames(CompositionPlane? plane, string? module, string lease)
    {
        var settings = LocalCoordinatorUnderTest.ReadSettings();
        var leases = new PostgreSqlIssuedLeases(new LocalLeaseCoordinator(settings, timeProvider: _clock), _clock);
        var postgreSql = new CoordinatorRegistration(CoordinatorKind.PostgreSql, CoordinatorCapabilities.PostgreSql, _ => new FakeHealth(), atomic: _ => new FakeAtomic(), leases: _ => leases, cache: _ => throw new InvalidOperationException("not selected"));
        var root = plane is { } named ? new CompositionRoot(named, module!) : null;
        var (startup, composed, services) = Compose("\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}", [postgreSql], root: root);

        await startup.StartAsync(services.Snapshot, Token);

        Assert.Equal(SingleInstanceState.Held, composed.Guard.State);
        Assert.Equal([lease], leases.Names);
        await startup.StopAsync(Token);
        await composed.DisposeAsync();
    }

    private (CoordinationStartup Startup, ComposedCoordination Composed, Services Services) Compose(
        string sectionFields,
        IReadOnlyList<CoordinatorRegistration>? registrations = null,
        IReadOnlyList<FeatureRequirement>? requirements = null,
        CompositionRoot? root = null)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"coordination\":{" + sectionFields + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        var settings = CoordinationSettings.From(report.Snapshot!);
        var services = new Services(report.Snapshot!);
        services.Add(settings);
        services.Add(new LocalAtomicOperations(settings, _clock));
        services.Add(new LocalLeaseCoordinator(settings, timeProvider: _clock));
        services.Add(new LocalVersionedCache(settings, timeProvider: _clock));
        if (root is not null)
        {
            services.Add(root);
        }

        var composed = new ComposedCoordination();
        return (new CoordinationStartup(composed, services, registrations ?? [], requirements ?? [], _loggers, _clock), composed, services);
    }

    private sealed class Services(ConfigurationSnapshot snapshot) : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = [];

        public ConfigurationSnapshot Snapshot { get; } = snapshot;

        public void Add<T>(T service)
            where T : notnull => _services[typeof(T)] = service;

        public T Get<T>() => (T)_services[typeof(T)];

        public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
    }

    // One capturing logger for Hosting's category, which event 6003 is written under, and one for everything else.
    private sealed class Loggers : ILoggerFactory
    {
        public CapturingDiagnostics.Logger Hosting { get; } = new();

        public CapturingDiagnostics.Logger Coordination { get; } = new();

        public ILogger CreateLogger(string categoryName) => categoryName == "Iskender.Hosting" ? Hosting : Coordination;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeHealth : ICoordinationHealthProbe
    {
        public ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(CoordinationHealthResult.Healthy);
    }

    private sealed class FakeAtomic : IAtomicOperations
    {
        public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // Local leases presented as PostgreSQL-issued, so the guard takes and holds them.
    private sealed class PostgreSqlIssuedLeases(ILeaseCoordinator inner, TimeProvider clock) : ILeaseCoordinator
    {
        private readonly Dictionary<Guid, Lease> _inner = [];
        private int _acquisitions;
        private int _releases;

        public int Acquisitions => Volatile.Read(ref _acquisitions);

        public List<string> Names { get; } = [];

        public int Releases => Volatile.Read(ref _releases);

        public async ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquisitions);
            Names.Add(name.Name);
            var acquisition = await inner.AcquireAsync(scope, name, duration, wait, cancellationToken);
            return acquisition.Lease is { } lease ? LeaseAcquisition.Acquired(Present(lease)) : acquisition;
        }

        public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) =>
            Present(await inner.RenewAsync(_inner[lease.Holder], duration, cancellationToken));

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _releases);
            return inner.ReleaseAsync(_inner[lease.Holder], cancellationToken);
        }

        private Lease Present(Lease lease)
        {
            _inner[lease.Holder] = lease;
            return Lease.Grant(lease.Scope, lease.Name, lease.Token, lease.Holder, CoordinatorKind.PostgreSql, lease.Remaining, clock.GetTimestamp(), lease.SafetyMargin, clock);
        }
    }
}

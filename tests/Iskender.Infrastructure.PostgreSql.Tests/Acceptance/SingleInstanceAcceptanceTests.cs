using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql.Tests.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Acceptance;

/// <summary>
/// User Story 1 scenarios 7 and 8, the single-instance half of FR-030's acceptance evidence: guards for two processes,
/// each with <c>PostgreSqlLeaseCoordinator</c> on a data source of its own, feeding the capability checker and the gate.
/// </summary>
public class SingleInstanceAcceptanceTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    // Renewed every second, so a stalled renewal has nearly two seconds before the lease is unsafe, and an expiry still
    // takes only a few seconds; within the coordinator's lease bounds.
    private static readonly TimeSpan InstanceLease = TimeSpan.FromSeconds(3);

    private static readonly FeatureRequirement SingletonJob = new(
        "fixture.singleton_job",
        "fixture",
        new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Local },
        mandatoryPath: false,
        OutageBehavior.FailClosed);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scenario7ASecondDataPlaneIsRefusedAndTheFirstKeepsServing()
    {
        coordinator.Require();
        await using var first = new SingleInstanceGuard(coordinator.Leases(0), CoordinatorKind.PostgreSql, SingleInstancePlane.DataPlane, InstanceLease, coordinator.Logger);
        await using var second = new SingleInstanceGuard(coordinator.Leases(1), CoordinatorKind.PostgreSql, SingleInstancePlane.DataPlane, InstanceLease, coordinator.Logger);
        await first.StartAsync(Token);
        await second.StartAsync(Token);

        var secondProcess = Process(second);
        Assert.Equal(SingleInstanceState.HeldByOther, second.State);
        Assert.False(secondProcess.Report.Find(SingletonJob.Name)!.Accepted);
        Assert.Equal(CapabilityReason.SingleInstanceViolated, secondProcess.Report.Find(SingletonJob.Name)!.Reason);

        // The first keeps serving across several renewals.
        var firstProcess = Process(first);
        Assert.True(firstProcess.Report.Find(SingletonJob.Name)!.Accepted);
        for (var call = 0; call < 4; call++)
        {
            await CallAsync(firstProcess.Gate);
            await Task.Delay(TimeSpan.FromMilliseconds(700), Token);
        }

        Assert.Equal(SingleInstanceState.Held, first.State);
        Assert.Equal(SingleInstanceState.HeldByOther, second.State);
        Assert.Equal(4, firstProcess.Leases.Calls);
    }

    [Fact]
    public async Task Scenario8AHolderWhoseLeaseExpiredFailsUntilItTakesTheLeaseAgain()
    {
        coordinator.Require();
        var leases = new WithheldRenewals(coordinator.Leases(0));
        await using var guard = new SingleInstanceGuard(leases, CoordinatorKind.PostgreSql, SingleInstancePlane.DataPlane, InstanceLease, coordinator.Logger);
        await guard.StartAsync(Token);
        var process = Process(guard);
        await CallAsync(process.Gate);

        // Renewal is withheld until the lease has expired on the server.
        leases.Withhold();
        await Task.Delay(InstanceLease + TimeSpan.FromMilliseconds(300), Token);

        Assert.Equal(SingleInstanceState.Lost, guard.State);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await CallAsync(process.Gate));
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, failure.Code);
        Assert.Equal(1, process.Leases.Calls);

        // Released, the withheld renewal finds the lease gone, and the next tick takes it again.
        leases.Resume();
        await WaitForAsync(guard, SingleInstanceState.Held);
        await CallAsync(process.Gate);
        Assert.Equal(2, process.Leases.Calls);
    }

    [Fact]
    public async Task AControlPlaneGuardDoesNotConflictWithADataPlaneGuard()
    {
        coordinator.Require();
        await using var data = new SingleInstanceGuard(coordinator.Leases(0), CoordinatorKind.PostgreSql, SingleInstancePlane.DataPlane, InstanceLease, coordinator.Logger);
        await using var control = new SingleInstanceGuard(coordinator.Leases(1), CoordinatorKind.PostgreSql, SingleInstancePlane.ControlPlane, InstanceLease, coordinator.Logger);

        await data.StartAsync(Token);
        await control.StartAsync(Token);

        Assert.Equal(SingleInstanceState.Held, data.State);
        Assert.Equal(SingleInstanceState.Held, control.State);
        Assert.True(Process(data).Report.Find(SingletonJob.Name)!.Accepted);
        Assert.True(Process(control).Report.Find(SingletonJob.Name)!.Accepted);
    }

    // One process's startup: the checker on single_instance with the guard's state, and the gate reading the guard.
    private (CapabilityReport Report, CoordinationGate Gate, CountingLeases Leases) Process(SingleInstanceGuard guard)
    {
        var capabilities = CoordinationPrimitives.All.ToDictionary(primitive => primitive, CoordinatorCapabilities? (_) => CoordinatorCapabilities.Local);
        var report = CoordinationCapabilityChecker.Check([SingletonJob], CoordinationTopology.SingleInstance, capabilities, guard.State);
        var leases = new CountingLeases(new LocalLeaseCoordinator(coordinator.CoordinationSettings));
        var unused = new UnusedPorts();
        return (report, new CoordinationGate(report, unused, leases, unused, fencing: null, guard), leases);
    }

    private static async Task CallAsync(CoordinationGate gate)
    {
        var name = new CoordinationName("fixture", "singleton_job");
        var lease = (await gate.ForFeature(SingletonJob.Name).Leases.AcquireAsync(CoordinationScope.Operator, name, TimeSpan.FromSeconds(1), LeaseWait.TryOnly, Token)).Lease;
        Assert.NotNull(lease);
        await gate.ForFeature(SingletonJob.Name).Leases.ReleaseAsync(lease, Token);
    }

    private static async Task WaitForAsync(SingleInstanceGuard guard, SingleInstanceState state)
    {
        for (var attempt = 0; attempt < 50 && guard.State != state; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        Assert.Equal(state, guard.State);
    }

    /// <summary>The PostgreSQL coordinator, with renewals that can be held back as a paused process would.</summary>
    private sealed class WithheldRenewals(ILeaseCoordinator inner) : ILeaseCoordinator
    {
        private volatile TaskCompletionSource _resumed = Completed();

        public void Withhold() => _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Resume() => _resumed.TrySetResult();

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken) =>
            inner.AcquireAsync(scope, name, duration, wait, cancellationToken);

        public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
        {
            await _resumed.Task.WaitAsync(cancellationToken);
            return await inner.RenewAsync(lease, duration, cancellationToken);
        }

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken) => inner.ReleaseAsync(lease, cancellationToken);

        private static TaskCompletionSource Completed()
        {
            var completed = new TaskCompletionSource();
            completed.SetResult();
            return completed;
        }
    }

    /// <summary>The feature's lease port, counting acquisitions that reach it.</summary>
    private sealed class CountingLeases(ILeaseCoordinator inner) : ILeaseCoordinator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return inner.AcquireAsync(scope, name, duration, wait, cancellationToken);
        }

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken) => inner.RenewAsync(lease, duration, cancellationToken);

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken) => inner.ReleaseAsync(lease, cancellationToken);
    }

    /// <summary>The ports the feature never uses.</summary>
    private sealed class UnusedPorts : IAtomicOperations, IVersionedCache
    {
        public ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

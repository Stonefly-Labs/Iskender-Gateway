using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Shards;

/// <summary>
/// The shard leases (ISK-0009 T019, <c>work</c> contract Dispatch and apply rules 2 and 3): names, the 64-key bound,
/// contention, the renewal cadence, and the lost-lease cancellation, against a recording lease coordinator.
/// </summary>
public class ShardLeasesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheLeaseAndFenceNameIsWorkDispatchShard()
    {
        Assert.Equal(new CoordinationName("work", "dispatch.0"), ShardLeases.Name(0));
        Assert.Equal(new CoordinationName("work", "dispatch.63"), ShardLeases.Name(63));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ShardLeases.Name(64)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ShardLeases.Name(-1)).Code);
    }

    [Fact]
    public void EveryBucketMapsToAShardBelowTheCountSoAScopeHasAtMost64Keys()
    {
        var names = new HashSet<CoordinationName>();
        foreach (var shards in new[] { 1, 2, 4, 8, 16, 32, 64 })
        {
            for (var bucket = 0; bucket < 64; bucket++)
            {
                var shard = ShardLeases.ShardOf(bucket, shards);
                Assert.Equal(bucket % shards, shard);
                names.Add(ShardLeases.Name(shard));
            }
        }

        Assert.Equal(64, names.Count);
        Assert.Throws<GatewayException>(() => ShardLeases.ShardOf(64, 4));
        Assert.Throws<GatewayException>(() => ShardLeases.ShardOf(0, 65));
    }

    [Fact]
    public async Task AContendedShardIsSkipped()
    {
        var coordinator = new RecordingLeases();
        var leases = new ShardLeases(coordinator, TimeSpan.FromMinutes(1), timeProvider: null);

        await using var held = await leases.TryAcquireAsync(WorkFixture.OrganizationA, 2, Token);
        var contended = await leases.TryAcquireAsync(WorkFixture.OrganizationA, 2, Token);
        await using var other = await leases.TryAcquireAsync(WorkFixture.OrganizationA, 3, Token);

        Assert.NotNull(held);
        Assert.Null(contended);
        Assert.NotNull(other);
        Assert.Equal(new CoordinationName("work", "dispatch.2"), held.Resource);
        Assert.All(coordinator.Acquisitions, wait => Assert.Null(wait.Bound));
    }

    [Fact]
    public async Task AHeldLeaseIsRenewedEveryThirdOfItsDuration()
    {
        var coordinator = new RecordingLeases();
        var duration = TimeSpan.FromMilliseconds(600);
        var leases = new ShardLeases(coordinator, duration, timeProvider: null);

        await using var held = await leases.TryAcquireAsync(WorkFixture.ProjectA1, 0, Token);
        await coordinator.RenewedAsync(3);
        await held!.ReleaseAsync(Token);

        var times = coordinator.RenewalTimes.Take(3).Prepend(coordinator.AcquiredAt).ToList();
        for (var index = 1; index < times.Count; index++)
        {
            Assert.True(times[index] - times[index - 1] >= duration / 3 - TimeSpan.FromMilliseconds(20), $"renewal {index} came early");
        }

        Assert.Equal(duration, Assert.Single(coordinator.RenewDurations.Distinct()));
        Assert.Equal(1, coordinator.Releases);
    }

    [Fact]
    public async Task ALostLeaseCancelsTheShardsTokenAndIsNotReleased()
    {
        var coordinator = new RecordingLeases { LoseOnRenewal = true };
        var leases = new ShardLeases(coordinator, TimeSpan.FromMilliseconds(300), timeProvider: null);

        await using var held = await leases.TryAcquireAsync(WorkFixture.OrganizationB, 1, Token);
        var lost = new TaskCompletionSource();
        using var registration = held!.Lost.Register(lost.SetResult);
        await lost.Task.WaitAsync(Token);

        Assert.True(held.IsLost);
        Assert.False(held.IsSafeToAct);
        await held.ReleaseAsync(Token);
        Assert.Equal(0, coordinator.Releases);
    }

    [Fact]
    public async Task AReleasedLeaseStopsRenewing()
    {
        var coordinator = new RecordingLeases();
        var leases = new ShardLeases(coordinator, TimeSpan.FromMilliseconds(300), timeProvider: null);

        var held = await leases.TryAcquireAsync(WorkFixture.OrganizationA, 5, Token);
        await held!.ReleaseAsync(Token);
        var renewals = coordinator.RenewalTimes.Count;
        await held.DisposeAsync();

        Assert.Equal(renewals, coordinator.RenewalTimes.Count);
        Assert.Equal(1, coordinator.Releases);
        Assert.NotNull(await leases.TryAcquireAsync(WorkFixture.OrganizationA, 5, Token));
    }

    // A lease coordinator that grants PostgreSQL-shaped leases in memory and records every call.
    private sealed class RecordingLeases : ILeaseCoordinator
    {
        private readonly ConcurrentDictionary<CoordinationName, Guid> _held = new();
        private readonly ConcurrentQueue<TaskCompletionSource> _waiters = new();
        private long _token;
        private int _releases;

        public bool LoseOnRenewal { get; init; }

        public ConcurrentQueue<LeaseWait> Acquisitions { get; } = new();

        public ConcurrentQueue<DateTimeOffset> RenewalTimes { get; } = new();

        public ConcurrentQueue<TimeSpan> RenewDurations { get; } = new();

        public DateTimeOffset AcquiredAt { get; private set; }

        public int Releases => _releases;

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
        {
            Acquisitions.Enqueue(wait);
            var holder = Guid.NewGuid();
            if (!_held.TryAdd(name, holder))
            {
                return ValueTask.FromResult(LeaseAcquisition.Contended);
            }

            AcquiredAt = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(LeaseAcquisition.Acquired(Grant(scope, name, holder, duration)));
        }

        public ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
        {
            if (LoseOnRenewal)
            {
                throw new GatewayException(ErrorCodes.CoordinationLeaseLost);
            }

            RenewalTimes.Enqueue(DateTimeOffset.UtcNow);
            RenewDurations.Enqueue(duration);
            foreach (var waiter in _waiters)
            {
                waiter.TrySetResult();
            }

            return ValueTask.FromResult(Grant(lease.Scope, lease.Name, lease.Holder, duration));
        }

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _releases);
            _held.TryRemove(new KeyValuePair<CoordinationName, Guid>(lease.Name, lease.Holder));
            return ValueTask.CompletedTask;
        }

        // Waits until at least `count` renewals were recorded, bounded by the test's token.
        public async Task RenewedAsync(int count)
        {
            while (RenewalTimes.Count < count)
            {
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Enqueue(waiter);
                if (RenewalTimes.Count >= count)
                {
                    break;
                }

                await waiter.Task.WaitAsync(Token);
            }
        }

        private Lease Grant(CoordinationScope scope, CoordinationName name, Guid holder, TimeSpan duration) =>
            Lease.Grant(scope, name, new FencingToken(Interlocked.Increment(ref _token)), holder, CoordinatorKind.PostgreSql, duration, TimeProvider.System.GetTimestamp(), TimeSpan.Zero, TimeProvider.System);
    }
}

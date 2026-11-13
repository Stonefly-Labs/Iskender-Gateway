using System.Collections.Concurrent;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Leases;

/// <summary>
/// The fencing conformance suite (FR-014, FR-017, FR-029): User Story 2 scenarios 2 and 4 and the fencing edge cases,
/// with leases from the implementation and writes through <see cref="IFenceUnderTest"/>, which is the PostgreSQL guard
/// for PostgreSQL leases and <see cref="RecordingFence"/> otherwise. A distributed implementation runs the contenders on
/// independent clients.
/// </summary>
public abstract class FencingConformanceTests
{
    protected FencingConformanceTests()
    {
        Module = "fence_" + Guid.NewGuid().ToString("N")[..12];
        Name = new CoordinationName(Module, "lease");
        Resource = new CoordinationName(Module, "resource");
    }

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    protected abstract IFenceUnderTest Fence { get; }

    protected string Module { get; }

    protected CoordinationName Name { get; }

    protected CoordinationName Resource { get; }

    /// <summary>How many clients contend in scenario 4.</summary>
    protected virtual int Contenders => 20;

    /// <summary>How many times each client acquires, writes, and releases in scenario 4.</summary>
    protected virtual int Rounds => 5;

    /// <summary>Extra time past an expiry, covering clock granularity on a distributed coordinator.</summary>
    protected virtual TimeSpan Slack => TimeSpan.FromMilliseconds(150);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    private TimeSpan Long => Coordinator.Settings.MaximumLeaseDuration < TimeSpan.FromSeconds(30) ? Coordinator.Settings.MaximumLeaseDuration : TimeSpan.FromSeconds(30);

    private TimeSpan Short => Coordinator.Settings.MinimumLeaseDuration;

    [Fact]
    public async Task Scenario2AnExpiredHoldersWriteIsRejectedOnceTheNextHolderHasFenced()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var stale = await Acquired(a, Short);
        await Coordinator.PassTimeAsync(Short + Slack);
        var current = await Acquired(b, Long);

        Assert.True(await Fence.WriteAsync(Resource, current, Token));
        Assert.False(await Fence.WriteAsync(Resource, stale, Token));

        Assert.Equal([current.Token.Value], await Fence.CommittedTokensAsync(Scope, Resource, Token));
        await b.Leases.ReleaseAsync(current, Token);
    }

    [Fact]
    public async Task Scenario4ConcurrentClientsNeverHoldAtOnceAndEveryAcceptedTokenIsAtLeastThePrevious()
    {
        Coordinator.Require();
        var holders = 0;
        var overlaps = 0;
        var issued = new ConcurrentBag<long>();
        var wait = LeaseWait.Bounded(Coordinator.Settings.MaximumWait);

        // Bounded waits poll on the coordinator's clock, so time keeps passing while the clients contend: a fake clock
        // moves 50 ms for each few real milliseconds, slowly enough that no 30-second lease expires mid-round.
        using var contending = new CancellationTokenSource();
        var clock = Task.Run(
            async () =>
            {
                while (!contending.IsCancellationRequested)
                {
                    await Coordinator.PassTimeAsync(TimeSpan.FromMilliseconds(50));
                    await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
                }
            },
            CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(0, Contenders).Select(index => Task.Run(
            async () =>
            {
                var client = Coordinator.CreateClient(index);
                for (var round = 0; round < Rounds; round++)
                {
                    var acquisition = await AcquireEventually(client, wait);
                    var lease = acquisition.Lease!;
                    issued.Add(lease.Token.Value);
                    if (Interlocked.Increment(ref holders) != 1)
                    {
                        Interlocked.Increment(ref overlaps);
                    }

                    Assert.True(await Fence.WriteAsync(Resource, lease, Token), "A current holder's write was rejected.");
                    Interlocked.Decrement(ref holders);
                    await client.Leases.ReleaseAsync(lease, Token);
                }
            },
            Token)));
        await contending.CancelAsync();
        await clock;

        var committed = await Fence.CommittedTokensAsync(Scope, Resource, Token);
        Assert.Equal(0, overlaps);
        Assert.Equal(Contenders * Rounds, issued.Count);
        Assert.Equal(issued.Count, issued.Distinct().Count());
        Assert.Equal(Contenders * Rounds, committed.Count);
        Assert.Equal(committed.Order(), committed);
    }

    [Fact]
    public async Task AnEqualTokenIsAccepted()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var lease = await Acquired(client, Long);

        Assert.True(await Fence.WriteAsync(Resource, lease, Token));
        Assert.True(await Fence.WriteAsync(Resource, lease, Token));

        Assert.Equal([lease.Token.Value, lease.Token.Value], await Fence.CommittedTokensAsync(Scope, Resource, Token));
        await client.Leases.ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task ATokenFromAnotherLeaseKeyIsRejected()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var first = await Acquired(client, Long);
        Assert.True(await Fence.WriteAsync(Resource, first, Token));

        // A lease on another name has a token of its own, which may well be higher, but it never fenced this resource.
        var other = (await client.Leases.AcquireAsync(Scope, new CoordinationName(Module, "other_lease"), Long, LeaseWait.TryOnly, Token)).Lease!;
        Assert.False(await Fence.WriteAsync(Resource, other, Token));

        Assert.Equal([first.Token.Value], await Fence.CommittedTokensAsync(Scope, Resource, Token));
        await client.Leases.ReleaseAsync(first, Token);
        await client.Leases.ReleaseAsync(other, Token);
    }

    [Fact]
    public async Task EachScopeFencesItsOwnResource()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var leases = new List<Lease>();
        foreach (var scope in Coordinator.Scopes.All)
        {
            leases.Add((await client.Leases.AcquireAsync(scope, Name, Long, LeaseWait.TryOnly, Token)).Lease!);
        }

        // Written newest first: a lower token in another scope is not stale, because the fence is per scope.
        foreach (var lease in leases.OrderByDescending(lease => lease.Token))
        {
            Assert.True(await Fence.WriteAsync(Resource, lease, Token));
        }

        foreach (var lease in leases)
        {
            Assert.Equal([lease.Token.Value], await Fence.CommittedTokensAsync(lease.Scope, Resource, Token));
            await client.Leases.ReleaseAsync(lease, Token);
        }
    }

    private async Task<Lease> Acquired(CoordinatorClient client, TimeSpan duration)
    {
        var acquisition = await client.Leases.AcquireAsync(Scope, Name, duration, LeaseWait.TryOnly, Token);
        Assert.True(acquisition.IsAcquired, "The lease was not free.");
        return acquisition.Lease;
    }

    // A bounded wait can end in contention under load; trying again keeps every round's result a real grant.
    private async Task<LeaseAcquisition> AcquireEventually(CoordinatorClient client, LeaseWait wait)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var acquisition = await client.Leases.AcquireAsync(Scope, Name, Long, wait, Token);
            if (acquisition.IsAcquired)
            {
                return acquisition;
            }
        }

        Assert.Fail("A contender never got the lease.");
        return LeaseAcquisition.Contended;
    }
}

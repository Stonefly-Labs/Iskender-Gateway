using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Acceptance;

/// <summary>
/// User Story 2 scenarios 1 to 4, the lease and fencing half of FR-030's acceptance evidence: a stale holder cannot
/// corrupt protected state. Each implementation's test project runs it with its own leases and fence, from two or more
/// independent clients.
/// </summary>
public abstract class LeaseFencingAcceptanceTests
{
    protected LeaseFencingAcceptanceTests()
    {
        var module = "accept_" + Guid.NewGuid().ToString("N")[..12];
        Name = new CoordinationName(module, "work");
        Resource = new CoordinationName(module, "protected");
    }

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    protected abstract IFenceUnderTest Fence { get; }

    protected CoordinationName Name { get; }

    protected CoordinationName Resource { get; }

    /// <summary>Extra time past an expiry, covering clock granularity on a distributed coordinator.</summary>
    protected virtual TimeSpan Slack => TimeSpan.FromMilliseconds(150);

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    protected TimeSpan ShortLease => Coordinator.Settings.MinimumLeaseDuration;

    protected TimeSpan LongLease => Coordinator.Settings.MaximumLeaseDuration < TimeSpan.FromSeconds(30) ? Coordinator.Settings.MaximumLeaseDuration : TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Scenario1TheNextHoldersTokenIsGreater()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var first = await AcquireAsync(a, ShortLease);
        await Coordinator.PassTimeAsync(ShortLease + Slack);

        var next = await AcquireAsync(b, LongLease);

        Assert.True(next.Token.Value > first.Token.Value);
        await b.Leases.ReleaseAsync(next, Token);
    }

    [Fact]
    public async Task Scenario2TheExpiredHoldersWriteIsRejectedAndCommitsNothing()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var stale = await AcquireAsync(a, ShortLease);
        await Coordinator.PassTimeAsync(ShortLease + Slack);
        var current = await AcquireAsync(b, LongLease);

        Assert.True(await Fence.WriteAsync(Resource, current, Token));
        Assert.False(await Fence.WriteAsync(Resource, stale, Token));

        Assert.Equal([current.Token.Value], await Fence.CommittedTokensAsync(Scope, Resource, Token));
        await b.Leases.ReleaseAsync(current, Token);
    }

    [Fact]
    public async Task Scenario3RenewingAnExpiredLeaseFailsWithLeaseLost()
    {
        Coordinator.Require();
        var a = Coordinator.CreateClient(0);
        var lease = await AcquireAsync(a, ShortLease);
        await Coordinator.PassTimeAsync(ShortLease + Slack);

        var lost = await Assert.ThrowsAsync<GatewayException>(async () => await a.Leases.RenewAsync(lease, ShortLease, Token));

        Assert.Same(ErrorCodes.CoordinationLeaseLost, lost.Code);
        Assert.Equal(CoordinationFailureClass.LeaseLost, CoordinationFailures.ClassOf(lost));
    }

    [Fact]
    public async Task Scenario4TwentyConcurrentClientsNeverHoldAtOnceAndNoTokenRepeatsOrGoesBackwards()
    {
        Coordinator.Require();
        const int Clients = 20;
        const int Rounds = 3;
        var holders = 0;
        var overlaps = 0;
        var issued = new ConcurrentBag<long>();
        var wait = LeaseWait.Bounded(Coordinator.Settings.MaximumWait);

        // Bounded waits poll on the coordinator's clock, so it keeps moving while the clients contend.
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

        await Task.WhenAll(Enumerable.Range(0, Clients).Select(index => Task.Run(
            async () =>
            {
                var client = Coordinator.CreateClient(index);
                for (var round = 0; round < Rounds; round++)
                {
                    var lease = await AcquireEventuallyAsync(client, wait);
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
        Assert.Equal(Clients * Rounds, issued.Count);
        Assert.Equal(issued.Count, issued.Distinct().Count());
        Assert.Equal(Clients * Rounds, committed.Count);
        Assert.Equal(committed.Order(), committed);
    }

    protected async Task<Lease> AcquireAsync(CoordinatorClient client, TimeSpan duration)
    {
        var acquisition = await client.Leases.AcquireAsync(Scope, Name, duration, LeaseWait.TryOnly, Token);
        Assert.True(acquisition.IsAcquired, "The lease was not free.");
        return acquisition.Lease!;
    }

    private async Task<Lease> AcquireEventuallyAsync(CoordinatorClient client, LeaseWait wait)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var acquisition = await client.Leases.AcquireAsync(Scope, Name, LongLease, wait, Token);
            if (acquisition.IsAcquired)
            {
                return acquisition.Lease!;
            }
        }

        Assert.Fail("A client never got the lease.");
        return null!;
    }
}

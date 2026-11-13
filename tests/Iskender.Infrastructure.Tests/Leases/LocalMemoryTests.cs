using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Leases;

/// <summary>
/// Contract Known limits ("No per-scope key count") and FR-013: the local substitutes' memory is bounded by expiry. A read
/// or release of an absent key adds nothing, and expired, released, and untouched keys are freed when next touched or by
/// the sweep a later operation runs.
/// </summary>
public class LocalMemoryTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadingOrReleasingAnAbsentKeyAddsNoEntry()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var scope = coordinator.Scopes.OrganizationA;

        for (var index = 0; index < 10; index++)
        {
            Assert.Null(await coordinator.Atomic.ReadAsync(scope, Name(index), Token));
            Assert.Null(await coordinator.Atomic.ReleaseAsync(scope, Name(index), 1, Token));
            Assert.False(await coordinator.Atomic.CompareAndSetAsync(scope, Name(index), 3, 1, Expiry, Token));
        }

        Assert.Equal(0, coordinator.Atomic.Count);
    }

    [Fact]
    public async Task ExpiredAtomicEntriesAreFreed()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var scope = coordinator.Scopes.OrganizationA;
        for (var index = 0; index < 10; index++)
        {
            await coordinator.Atomic.AddAsync(scope, Name(index), 1, Expiry, Token);
        }

        Assert.Equal(10, coordinator.Atomic.Count);
        coordinator.Clock.Advance(Expiry + LocalEntry.SweepInterval);

        // A read of one expired key frees it, and the sweep that operation runs frees the nine nobody touched.
        Assert.Null(await coordinator.Atomic.ReadAsync(scope, Name(0), Token));
        Assert.Equal(0, coordinator.Atomic.Count);

        // A live key is kept, and a new write to an expired key starts again at version 1.
        Assert.Equal(new CoordinationValue(2, 1), await coordinator.Atomic.AddAsync(scope, Name(1), 2, Expiry, Token));
        Assert.Equal(1, coordinator.Atomic.Count);
    }

    [Fact]
    public async Task ReleasedAndExpiredLeaseSlotsAreFreed()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var scope = coordinator.Scopes.OrganizationA;
        var leases = new List<Lease>();
        for (var index = 0; index < 10; index++)
        {
            leases.Add((await coordinator.Leases.AcquireAsync(scope, Name(index), Expiry, LeaseWait.TryOnly, Token)).Lease!);
        }

        Assert.Equal(10, coordinator.Leases.Count);
        await coordinator.Leases.ReleaseAsync(leases[0], Token);
        Assert.Equal(9, coordinator.Leases.Count);

        // A contended acquire keeps the holder's slot; once every lease has expired, the next operation's sweep frees them.
        Assert.False((await coordinator.Leases.AcquireAsync(scope, Name(1), Expiry, LeaseWait.TryOnly, Token)).IsAcquired);
        Assert.Equal(9, coordinator.Leases.Count);
        coordinator.Clock.Advance(Expiry + LocalEntry.SweepInterval);
        var next = (await coordinator.Leases.AcquireAsync(scope, Name(20), Expiry, LeaseWait.TryOnly, Token)).Lease!;

        Assert.Equal(1, coordinator.Leases.Count);
        Assert.True(next.Token.Value > leases[^1].Token.Value);
    }

    [Fact]
    public async Task AKeyFreedWhileOthersUseItIsNeverHeldTwice()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var scope = coordinator.Scopes.OrganizationA;
        var name = Name(0);
        var holders = 0;
        var overlapped = false;

        await Parallel.ForAsync(0, 2000, Token, async (_, cancellationToken) =>
        {
            var acquisition = await coordinator.Leases.AcquireAsync(scope, name, Expiry, LeaseWait.TryOnly, cancellationToken);
            if (!acquisition.IsAcquired)
            {
                return;
            }

            overlapped |= Interlocked.Increment(ref holders) > 1;
            Interlocked.Decrement(ref holders);
            await coordinator.Leases.ReleaseAsync(acquisition.Lease!, cancellationToken);
        });

        Assert.False(overlapped);
        Assert.Equal(0, coordinator.Leases.Count);
    }

    private static CoordinationName Name(int index) => new("memory", $"key_{index}");
}

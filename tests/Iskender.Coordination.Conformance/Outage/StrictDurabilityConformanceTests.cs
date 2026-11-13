using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Outage;

/// <summary>
/// Holds a coordinator to the levels it declares across a restart (spec Edge Cases, TR-17, FR-002a): for each primitive
/// declared <c>strict</c>, what was written before the coordinator restarts is still there afterwards. A counter keeps its
/// value and version, a namespace keeps its generation, a held lease stays held, and every token granted after the restart
/// is above every token granted before it. A primitive declared below <c>strict</c> may lose writes on a restart, so it is
/// not checked. It runs only against an implementation whose <see cref="ICoordinatorUnderTest.SupportsFaults"/>, which
/// owns its coordinator, and reads afterwards through a client created after the restart.
/// </summary>
public abstract class StrictDurabilityConformanceTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly string _module = "durable_" + Guid.NewGuid().ToString("N")[..12];

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    [Fact]
    public async Task AStrictCoordinatorLosesNothingAcrossARestart()
    {
        Coordinator.Require();
        Assert.True(Coordinator.SupportsFaults, "The durability suite runs only against a coordinator under test that supports faults.");
        var strict = Enum.GetValues<CoordinationPrimitive>().Where(primitive => Coordinator.Capabilities.LevelOf(primitive) == ConsistencyLevel.Strict).ToHashSet();
        Assert.NotEmpty(strict);

        var before = Coordinator.CreateClient(0);
        var counter = new CoordinationName(_module, "counter");
        var held = new CoordinationName(_module, "held");
        var released = new CoordinationName(_module, "released");
        var cacheNamespace = new CacheNamespace(_module, "entries");
        CoordinationValue? written = null;
        Lease? holding = null;
        var tokensBefore = new List<long>();
        CacheStamp? generation = null;

        if (strict.Contains(CoordinationPrimitive.AtomicOperations))
        {
            await before.Atomic.AddAsync(Scope, counter, 3, Expiry, Token);
            written = await before.Atomic.AddAsync(Scope, counter, 4, Expiry, Token);
        }

        if (strict.Contains(CoordinationPrimitive.Leases) || strict.Contains(CoordinationPrimitive.Fencing))
        {
            var taken = (await before.Leases.AcquireAsync(Scope, released, Coordinator.Settings.MinimumLeaseDuration, LeaseWait.TryOnly, Token)).Lease!;
            await before.Leases.ReleaseAsync(taken, Token);
            holding = (await before.Leases.AcquireAsync(Scope, held, Coordinator.Settings.MaximumLeaseDuration, LeaseWait.TryOnly, Token)).Lease!;
            tokensBefore.AddRange([taken.Token.Value, holding.Token.Value]);
        }

        if (strict.Contains(CoordinationPrimitive.CacheInvalidation))
        {
            await before.Cache.InvalidateAsync(Scope, cacheNamespace, Token);
            await before.Cache.InvalidateAsync(Scope, cacheNamespace, Token);
            generation = (await before.Cache.TryGetAsync(Scope, cacheNamespace, "entry", 1, Token)).Stamp;
            Assert.NotNull(generation);
            Assert.NotEqual(0, generation.Value.Generation);
        }

        await Coordinator.StopAsync();
        await Coordinator.ResumeAsync();
        var after = Coordinator.CreateClient(1000 + Random.Shared.Next(1000));

        if (written is { } value)
        {
            Assert.Equal(value, await after.Atomic.ReadAsync(Scope, counter, Token));
        }

        if (holding is not null)
        {
            Assert.False((await after.Leases.AcquireAsync(Scope, held, Coordinator.Settings.MinimumLeaseDuration, LeaseWait.TryOnly, Token)).IsAcquired);
            var regranted = (await after.Leases.AcquireAsync(Scope, released, Coordinator.Settings.MinimumLeaseDuration, LeaseWait.TryOnly, Token)).Lease!;
            Assert.True(regranted.Token.Value > tokensBefore.Max(), $"Token {regranted.Token.Value} was granted after a restart, not above {tokensBefore.Max()}.");
            await after.Leases.ReleaseAsync(regranted, Token);
            await after.Leases.ReleaseAsync(holding, Token);
        }

        if (generation is { } stamp)
        {
            Assert.Equal(stamp, (await after.Cache.TryGetAsync(Scope, cacheNamespace, "entry", 1, Token)).Stamp);
        }
    }
}

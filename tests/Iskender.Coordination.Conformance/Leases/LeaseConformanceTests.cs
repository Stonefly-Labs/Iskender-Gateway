using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Leases;

/// <summary>
/// The lease conformance suite (FR-014 to FR-016, FR-029): User Story 2 scenarios 1, 3, 5, 6, and 7 and the lease edge
/// cases, run against each implementation through <see cref="ICoordinatorUnderTest"/>. A distributed implementation runs
/// the two contenders on two independent clients. Each test instance uses a module of its own, so its keys and its
/// events are its own.
/// </summary>
public abstract class LeaseConformanceTests
{
    protected LeaseConformanceTests()
    {
        Module = "conf_" + Guid.NewGuid().ToString("N")[..12];
        Name = new CoordinationName(Module, "lease");
    }

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    /// <summary>This test instance's module.</summary>
    protected string Module { get; }

    protected CoordinationName Name { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    /// <summary>The longest lease the suite takes, so that a lease never expires during a step that does not expect it.</summary>
    private TimeSpan Long => Min(Coordinator.Settings.MaximumLeaseDuration, TimeSpan.FromSeconds(30));

    private TimeSpan Short => Coordinator.Settings.MinimumLeaseDuration;

    [Fact]
    public async Task Scenario1AfterALeaseExpiresTheNextHolderGetsAGreaterToken()
    {
        Coordinator.Require();
        var (a, b) = Contenders();

        var first = await Acquired(a, Short);
        await Coordinator.PassTimeAsync(Short + Slack);
        var second = await Acquired(b, Long);

        Assert.True(second.Token > first.Token);
        await b.Leases.ReleaseAsync(second, Token);
    }

    [Fact]
    public async Task Scenario3RenewalAfterExpiryFailsWithLeaseLostAndWritesEvent5005()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var lease = await Acquired(a, Short);
        await Coordinator.PassTimeAsync(Short + Slack);

        // Whether or not another holder took it meanwhile, the renewal fails.
        var taken = await Acquired(b, Long);
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await a.Leases.RenewAsync(lease, Long, Token));

        Assert.Same(ErrorCodes.CoordinationLeaseLost, failure.Code);
        Assert.Equal(CoordinationFailureClass.LeaseLost, CoordinationFailures.ClassOf(failure));
        Assert.Contains(CapturingDiagnostics.Shared.Logs, logged => logged.Id.Id == 5005 && Field(logged, "Module") == Module);
        await b.Leases.ReleaseAsync(taken, Token);
    }

    [Fact]
    public async Task RenewalOfAHeldLeaseKeepsItsToken()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var lease = await Acquired(client, Short);

        var renewed = await client.Leases.RenewAsync(lease, Long, Token);

        Assert.Equal(lease.Token, renewed.Token);
        Assert.Equal(lease.Holder, renewed.Holder);
        Assert.True(renewed.Remaining > Short);
        await client.Leases.ReleaseAsync(renewed, Token);
    }

    [Fact]
    public async Task Scenario5TryOnlyReportsContentionAtOnce()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var held = await Acquired(a, Long);

        var contended = await b.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.TryOnly, Token);

        Assert.False(contended.IsAcquired);
        Assert.Same(LeaseAcquisition.Contended, contended);
        await a.Leases.ReleaseAsync(held, Token);
    }

    [Fact]
    public async Task Scenario5ABoundedWaitGetsTheLeaseNoLaterThanTheBoundAfterRelease()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var bound = Bound;
        var held = await Acquired(a, Long);
        var waiting = b.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.Bounded(bound), Token).AsTask();
        await Coordinator.PassTimeAsync(PollStep);
        Assert.False(waiting.IsCompleted);

        await a.Leases.ReleaseAsync(held, Token);
        var acquisition = await Settle(waiting, bound);

        Assert.True(acquisition.IsAcquired);
        Assert.True(acquisition.Lease.Token > held.Token);
        await b.Leases.ReleaseAsync(acquisition.Lease, Token);
    }

    [Fact]
    public async Task Scenario5ABoundedWaitReportsContentionAtTheBound()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var bound = Bound;
        var held = await Acquired(a, Long);

        var acquisition = await Settle(b.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.Bounded(bound), Token).AsTask(), bound);

        Assert.False(acquisition.IsAcquired);
        await a.Leases.ReleaseAsync(held, Token);
    }

    [Fact]
    public async Task Scenario6DoubleReleaseCompletesWithoutError()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var lease = await Acquired(client, Long);

        await client.Leases.ReleaseAsync(lease, Token);
        await client.Leases.ReleaseAsync(lease, Token);

        var again = await Acquired(client, Long);
        await client.Leases.ReleaseAsync(again, Token);
    }

    [Fact]
    public async Task Scenario6ReleaseAfterLossDoesNotReleaseAnotherHoldersLease()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var lost = await Acquired(a, Short);
        await Coordinator.PassTimeAsync(Short + Slack);
        var current = await Acquired(b, Long);

        await a.Leases.ReleaseAsync(lost, Token);

        Assert.False((await a.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.TryOnly, Token)).IsAcquired);
        var renewed = await b.Leases.RenewAsync(current, Long, Token);
        Assert.Equal(current.Token, renewed.Token);
        await b.Leases.ReleaseAsync(renewed, Token);
    }

    [Fact]
    public async Task Scenario7TheSameNameInDifferentScopesNeverContends()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var scopes = Coordinator.Scopes.All;

        var leases = new List<Lease>();
        for (var index = 0; index < scopes.Count; index++)
        {
            var client = index % 2 == 0 ? a : b;
            var acquisition = await client.Leases.AcquireAsync(scopes[index], Name, Long, LeaseWait.TryOnly, Token);
            Assert.True(acquisition.IsAcquired, $"Scope {index} waited for another scope's lease.");
            leases.Add(acquisition.Lease);
        }

        // Each scope's lease is its own: releasing one leaves the others held, and a new holder in the released scope
        // gets a token greater than the one it replaced.
        await a.Leases.ReleaseAsync(leases[0], Token);
        for (var index = 1; index < scopes.Count; index++)
        {
            Assert.False((await a.Leases.AcquireAsync(scopes[index], Name, Long, LeaseWait.TryOnly, Token)).IsAcquired);
        }

        var replaced = await Acquired(b, Long, scopes[0]);
        Assert.True(replaced.Token > leases[0].Token);
        await b.Leases.ReleaseAsync(replaced, Token);
        for (var index = 1; index < scopes.Count; index++)
        {
            await (index % 2 == 0 ? a : b).Leases.ReleaseAsync(leases[index], Token);
        }
    }

    [Fact]
    public async Task ADurationOutsideTheConfiguredBoundsFailsWithValueInvalid()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var settings = Coordinator.Settings;

        foreach (var duration in new[] { settings.MinimumLeaseDuration - TimeSpan.FromMilliseconds(1), settings.MaximumLeaseDuration + TimeSpan.FromMilliseconds(1), TimeSpan.Zero, -Long })
        {
            var failure = await Assert.ThrowsAsync<GatewayException>(async () => await client.Leases.AcquireAsync(Scope, Name, duration, LeaseWait.TryOnly, Token));
            Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        }

        var lease = await Acquired(client, Long);
        var renewal = await Assert.ThrowsAsync<GatewayException>(async () => await client.Leases.RenewAsync(lease, settings.MaximumLeaseDuration + TimeSpan.FromMilliseconds(1), Token));
        Assert.Same(ErrorCodes.ValueInvalid, renewal.Code);
        await client.Leases.ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task AWaitAboveTheMaximumFailsWithValueInvalid()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () =>
            await client.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.Bounded(Coordinator.Settings.MaximumWait + TimeSpan.FromMilliseconds(1)), Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.True((await client.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.TryOnly, Token)).IsAcquired, "The refused call took the lease.");
    }

    [Fact]
    public async Task ALeaseIsNotSafeToActOnceRemainingIsAtOrBelowTheSafetyMargin()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var margin = Coordinator.Settings.SafetyMargin;
        var duration = Min(Coordinator.Settings.MaximumLeaseDuration, Max(Short, margin + TimeSpan.FromSeconds(1)));
        var lease = await Acquired(client, duration);

        Assert.Equal(margin, lease.SafetyMargin);
        Assert.True(lease.IsSafeToAct);
        Assert.True(lease.Remaining <= duration);

        await Coordinator.PassTimeAsync(duration - margin);

        Assert.True(lease.Remaining <= margin);
        Assert.False(lease.IsSafeToAct);
        await client.Leases.ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task APassedDeadlineFailsWithTimeout()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);
        var expired = Coordinator.Scopes.WithDeadline(DateTimeOffset.UnixEpoch);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await client.Leases.AcquireAsync(expired, Name, Long, LeaseWait.TryOnly, Token));

        Assert.Same(ErrorCodes.CoordinationTimeout, failure.Code);
        Assert.True((await client.Leases.AcquireAsync(Coordinator.Scopes.WithDeadline(DateTimeOffset.MaxValue), Name, Long, LeaseWait.TryOnly, Token)).IsAcquired);
    }

    [Fact]
    public async Task CancellationDuringABoundedWaitStopsItWithCancelledAndLeavesNoLeaseHeld()
    {
        Coordinator.Require();
        var (a, b) = Contenders();
        var held = await Acquired(a, Long);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = b.Leases.AcquireAsync(Scope, Name, Long, LeaseWait.Bounded(Coordinator.Settings.MaximumWait), cancellation.Token).AsTask();
        await Coordinator.PassTimeAsync(PollStep);

        await cancellation.CancelAsync();
        var failure = await Assert.ThrowsAsync<GatewayException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(30), Token));

        Assert.Same(ErrorCodes.CoordinationCancelled, failure.Code);
        await a.Leases.ReleaseAsync(held, Token);
        var after = await Acquired(a, Long);
        await a.Leases.ReleaseAsync(after, Token);
    }

    [Fact]
    public async Task ANullScopeFailsWithScopeRequired()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await client.Leases.AcquireAsync(null!, Name, Long, LeaseWait.TryOnly, Token));

        Assert.Same(ErrorCodes.CoordinationScopeRequired, failure.Code);
    }

    [Fact]
    public async Task ANewLeaseCarriesItsIssuerAndShowsNoScopeOrHolder()
    {
        Coordinator.Require();
        var client = Coordinator.CreateClient(0);

        var lease = await Acquired(client, Long);

        Assert.Equal(Coordinator.Kind, lease.Issuer);
        Assert.NotEqual(Guid.Empty, lease.Holder);
        Assert.DoesNotContain(lease.Holder.ToString(), lease.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Coordinator.Scopes.OrganizationAScope.Organization.ToString(), lease.ToString(), StringComparison.Ordinal);
        await client.Leases.ReleaseAsync(lease, Token);
    }

    /// <summary>Extra time past an expiry, covering clock granularity on a distributed coordinator.</summary>
    protected virtual TimeSpan Slack => TimeSpan.FromMilliseconds(150);

    /// <summary>How far time moves between checks while a bounded wait runs.</summary>
    protected virtual TimeSpan PollStep => TimeSpan.FromMilliseconds(100);

    private TimeSpan Bound => Min(Coordinator.Settings.MaximumWait, TimeSpan.FromSeconds(2));

    private (CoordinatorClient A, CoordinatorClient B) Contenders() => (Coordinator.CreateClient(0), Coordinator.CreateClient(1));

    private async Task<Lease> Acquired(CoordinatorClient client, TimeSpan duration, CoordinationScope? scope = null)
    {
        var acquisition = await client.Leases.AcquireAsync(scope ?? Scope, Name, duration, LeaseWait.TryOnly, Token);
        Assert.True(acquisition.IsAcquired, "The lease was not free.");
        return acquisition.Lease;
    }

    // Lets time pass in steps until the wait ends, allowing the bound plus a few steps before failing.
    private async Task<LeaseAcquisition> Settle(Task<LeaseAcquisition> waiting, TimeSpan bound)
    {
        var passed = TimeSpan.Zero;
        while (!waiting.IsCompleted && passed <= bound + (4 * PollStep))
        {
            await Coordinator.PassTimeAsync(PollStep);
            passed += PollStep;
        }

        return await waiting.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    private static string? Field(CapturedLog logged, string name) => logged.Fields.FirstOrDefault(field => field.Key == name).Value as string;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}

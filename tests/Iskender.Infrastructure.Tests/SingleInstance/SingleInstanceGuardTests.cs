using Iskender.Coordination.Conformance;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.SingleInstance;

public class SingleInstanceGuardTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    private readonly LocalCoordinatorUnderTest _coordinator = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutALeaseCoordinatorItIsUnverifiedAndWritesNoEvent()
    {
        await using var guard = new SingleInstanceGuard(null, SingleInstancePlane.DataPlane, Duration, _coordinator.Logger, _coordinator.Clock);

        await guard.StartAsync(Token);
        await PassAsync(Duration * 3);

        Assert.Equal(SingleInstanceState.Unverified, guard.State);
        Assert.Empty(Changes());
    }

    [Fact]
    public async Task TheFirstGuardHoldsTheLeaseAndTheSecondReportsHeldByOther()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        await using var first = Guard(leases, SingleInstancePlane.DataPlane);
        await using var second = Guard(leases, SingleInstancePlane.DataPlane);

        await first.StartAsync(Token);
        await second.StartAsync(Token);

        Assert.Equal(SingleInstanceState.Held, first.State);
        Assert.Equal(SingleInstanceState.HeldByOther, second.State);
        Assert.Equal([("held", "unverified"), ("held_by_other", "unverified")], Changes());
    }

    [Fact]
    public async Task ItRenewsEveryThirdOfTheDurationAndStaysHeldWellPastIt()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        await using var guard = Guard(leases, SingleInstancePlane.DataPlane);
        await using var other = Guard(leases, SingleInstancePlane.DataPlane);
        await guard.StartAsync(Token);
        await other.StartAsync(Token);
        Assert.Equal(TimeSpan.FromSeconds(10), guard.RenewalPeriod);

        await PassAsync(TimeSpan.FromSeconds(9));
        Assert.Equal(0, leases.Renewals);
        for (var period = 1; period <= 9; period++)
        {
            await PassAsync(period == 1 ? TimeSpan.FromSeconds(1) : guard.RenewalPeriod);
            Assert.Equal(period, leases.Renewals);
        }

        Assert.Equal(SingleInstanceState.Held, guard.State);
        Assert.Equal(SingleInstanceState.HeldByOther, other.State);
        Assert.Equal(2, Changes().Count);
    }

    [Fact]
    public async Task AFailedRenewalIsLostUntilTheLeaseIsTakenAgain()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        await using var guard = Guard(leases, SingleInstancePlane.DataPlane);
        await guard.StartAsync(Token);

        leases.FailRenewals = true;
        await PassAsync(guard.RenewalPeriod);
        Assert.Equal(SingleInstanceState.Lost, guard.State);
        leases.FailRenewals = false;

        // The old lease still holds the key until it expires, 30 seconds after it was last granted.
        await PassAsync(guard.RenewalPeriod);
        Assert.Equal(SingleInstanceState.Lost, guard.State);
        await PassAsync(guard.RenewalPeriod);
        Assert.Equal(SingleInstanceState.Held, guard.State);
        await PassAsync(guard.RenewalPeriod * 3);
        Assert.Equal(SingleInstanceState.Held, guard.State);

        Assert.Equal([("held", "unverified"), ("lost", "held"), ("held", "lost")], Changes());
    }

    [Fact]
    public async Task AHeldLeaseNoLongerSafeToActOnIsReportedLost()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        await using var guard = Guard(leases, SingleInstancePlane.DataPlane);
        await guard.StartAsync(Token);

        // Renewals hang, so the timer cannot move the state; reading it must.
        leases.HangRenewals = true;
        await PassAsync(Duration - _coordinator.CoordinationSettings.SafetyMargin + TimeSpan.FromSeconds(1));

        Assert.Equal(SingleInstanceState.Lost, guard.State);
        Assert.Equal([("held", "unverified"), ("lost", "held")], Changes());
        leases.HangRenewals = false;
    }

    [Fact]
    public async Task AnUnreachableCoordinatorAtStartIsLostNotHeldByOther()
    {
        var leases = new ControlledLeases(_coordinator.Leases) { FailAcquisitions = true };
        await using var guard = Guard(leases, SingleInstancePlane.DataPlane);

        await guard.StartAsync(Token);
        Assert.Equal(SingleInstanceState.Lost, guard.State);

        leases.FailAcquisitions = false;
        await PassAsync(guard.RenewalPeriod);
        Assert.Equal(SingleInstanceState.Held, guard.State);
        Assert.Equal([("lost", "unverified"), ("held", "lost")], Changes());
    }

    [Fact]
    public async Task DisposingReleasesTheLease()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        var first = Guard(leases, SingleInstancePlane.DataPlane);
        await using var second = Guard(leases, SingleInstancePlane.DataPlane);
        await first.StartAsync(Token);
        await second.StartAsync(Token);

        await first.DisposeAsync();
        await PassAsync(second.RenewalPeriod);

        Assert.Equal(SingleInstanceState.Held, second.State);
        Assert.Equal(1, leases.Releases);
    }

    [Fact]
    public async Task TheDataAndControlPlanesDoNotConflict()
    {
        var leases = new ControlledLeases(_coordinator.Leases);
        await using var data = Guard(leases, SingleInstancePlane.DataPlane);
        await using var control = Guard(leases, SingleInstancePlane.ControlPlane);

        await data.StartAsync(Token);
        await control.StartAsync(Token);

        Assert.Equal(SingleInstanceState.Held, data.State);
        Assert.Equal(SingleInstanceState.Held, control.State);
        Assert.Equal(new CoordinationName("coordination", "instance.data_plane"), SingleInstanceGuard.NameOf(SingleInstancePlane.DataPlane));
        Assert.Equal(new CoordinationName("coordination", "instance.control_plane"), SingleInstanceGuard.NameOf(SingleInstancePlane.ControlPlane));
    }

    [Fact]
    public void AnInvalidPlaneOrDurationIsRefused()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new SingleInstanceGuard(null, (SingleInstancePlane)7, Duration)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new SingleInstanceGuard(null, SingleInstancePlane.DataPlane, TimeSpan.Zero)).Code);
    }

    private SingleInstanceGuard Guard(ILeaseCoordinator leases, SingleInstancePlane plane) =>
        new(leases, plane, Duration, _coordinator.Logger, _coordinator.Clock);

    // Each timer tick runs asynchronously, so advance one second at a time and let it finish.
    private async Task PassAsync(TimeSpan duration)
    {
        for (var passed = TimeSpan.Zero; passed < duration; passed += TimeSpan.FromSeconds(1))
        {
            await _coordinator.PassTimeAsync(TimeSpan.FromSeconds(1) < duration - passed ? TimeSpan.FromSeconds(1) : duration - passed);
            await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
        }
    }

    private List<(string State, string Previous)> Changes() =>
        [.. _coordinator.Logger.Events.Where(logged => logged.Id.Id == 5011).Select(logged => (Field(logged, "Outcome"), Field(logged, "Reason")))];

    private static string Field(CapturedLog logged, string name) => logged.Fields.Single(field => field.Key == name).Value?.ToString() ?? "";

    /// <summary>The local coordinator, counting renewals and releases, with renewals and acquisitions that can be made to fail or hang.</summary>
    private sealed class ControlledLeases(ILeaseCoordinator inner) : ILeaseCoordinator
    {
        private int _renewals;
        private int _releases;

        public bool FailRenewals { get; set; }

        public bool HangRenewals { get; set; }

        public bool FailAcquisitions { get; set; }

        public int Renewals => Volatile.Read(ref _renewals);

        public int Releases => Volatile.Read(ref _releases);

        public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken) =>
            FailAcquisitions
                ? throw CoordinationFailures.Raise(ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable)
                : inner.AcquireAsync(scope, name, duration, wait, cancellationToken);

        public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _renewals);
            if (HangRenewals)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return FailRenewals
                ? throw CoordinationFailures.Raise(ErrorCodes.CoordinationUnavailable, CoordinationFailureClass.Unavailable)
                : await inner.RenewAsync(lease, duration, cancellationToken);
        }

        public ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _releases);
            return inner.ReleaseAsync(lease, cancellationToken);
        }
    }
}

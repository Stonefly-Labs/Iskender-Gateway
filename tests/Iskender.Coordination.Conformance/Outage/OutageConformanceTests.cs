using System.Diagnostics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Outage;

/// <summary>
/// Classification of a stopped and a paused coordinator (TR-17, FR-022, FR-029): a stopped coordinator fails with
/// <c>coordination.unavailable</c>, and a paused one with <c>coordination.timeout</c>, each within <c>operationTimeout</c>
/// plus the fixed 2-second allowance and never as granted. It runs only against an implementation whose
/// <see cref="ICoordinatorUnderTest.SupportsFaults"/>, which owns its coordinator, and puts it back afterwards.
/// </summary>
public abstract class OutageConformanceTests
{
    // The contract's fixed safety bound past operationTimeout.
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    private readonly string _module = "outage_" + Guid.NewGuid().ToString("N")[..12];

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    [Fact]
    public async Task AStoppedCoordinatorIsUnavailableWithinTheBound()
    {
        Require();
        var client = Coordinator.CreateClient(0);
        var counter = new CoordinationName(_module, "stopped");
        Assert.True((await client.Atomic.ReserveAsync(Scope, counter, 1, 100, Expiry, Token)).Granted);

        await Coordinator.StopAsync();
        try
        {
            AssertUnavailable(await FailAsync(async () => await client.Atomic.ReserveAsync(Scope, counter, 1, 100, Expiry, Token)));
            AssertUnavailable(await FailAsync(async () => await client.Leases.AcquireAsync(Scope, new CoordinationName(_module, "stopped_lease"), Coordinator.Settings.MinimumLeaseDuration, LeaseWait.TryOnly, Token)));
        }
        finally
        {
            await Coordinator.ResumeAsync();
        }
    }

    [Fact]
    public async Task APausedCoordinatorTimesOutWithinTheBound()
    {
        Require();
        var client = Coordinator.CreateClient(0);
        var counter = new CoordinationName(_module, "paused");
        Assert.True((await client.Atomic.ReserveAsync(Scope, counter, 1, 100, Expiry, Token)).Granted);

        await Coordinator.PauseAsync();
        try
        {
            var (failure, elapsed) = await FailAsync(async () => await client.Atomic.ReadAsync(Scope, counter, Token));

            Assert.Same(ErrorCodes.CoordinationTimeout, failure.Code);
            Assert.Equal(CoordinationFailureClass.Timeout, CoordinationFailures.ClassOf(failure));
            Assert.InRange(elapsed, TimeSpan.Zero, Coordinator.Settings.OperationTimeout + Allowance);

            // A read changes nothing, so its outcome is never unknown.
            Assert.False(CoordinationFailures.IsOutcomeUnknown(failure));
        }
        finally
        {
            await Coordinator.ResumeAsync();
        }
    }

    private void Require()
    {
        Coordinator.Require();
        Assert.True(Coordinator.SupportsFaults, "The outage suite runs only against a coordinator under test that supports faults.");
    }

    private void AssertUnavailable((GatewayException Failure, TimeSpan Elapsed) result)
    {
        Assert.Same(ErrorCodes.CoordinationUnavailable, result.Failure.Code);
        Assert.Equal(CoordinationFailureClass.Unavailable, CoordinationFailures.ClassOf(result.Failure));
        Assert.InRange(result.Elapsed, TimeSpan.Zero, Coordinator.Settings.OperationTimeout + Allowance);
    }

    private static async Task<(GatewayException Failure, TimeSpan Elapsed)> FailAsync(Func<Task> call)
    {
        var watch = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<GatewayException>(call);
        return (failure, watch.Elapsed);
    }
}

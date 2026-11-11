using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class LeaseTests
{
    private static readonly CoordinationName Name = new("worker", "outbox.relay");

    [Fact]
    public void RemainingIsTheGrantedTimeLessTheTimeSinceSend()
    {
        var clock = new SteppingClock();
        var sent = clock.GetTimestamp();
        clock.Advance(TimeSpan.FromMilliseconds(300));

        var lease = Grant(clock, TimeSpan.FromSeconds(10), sent);

        Assert.Equal(TimeSpan.FromMilliseconds(9700), lease.Remaining);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromMilliseconds(7700), lease.Remaining);
    }

    [Fact]
    public void NetworkDelayOnlyShortensRemaining()
    {
        var clock = new SteppingClock();
        var sent = clock.GetTimestamp();
        var granted = TimeSpan.FromSeconds(10);

        // The reply arrives after 1 s on the wire, so the coordinator's true remaining time is at most the granted time.
        clock.Advance(TimeSpan.FromSeconds(1));
        var lease = Grant(clock, granted, sent);

        Assert.True(lease.Remaining <= granted);
        Assert.Equal(granted - TimeSpan.FromSeconds(1), lease.Remaining);
    }

    [Fact]
    public void RemainingNeverGoesBelowZero()
    {
        var clock = new SteppingClock();
        var lease = Grant(clock, TimeSpan.FromSeconds(1), clock.GetTimestamp());

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, lease.Remaining);
        Assert.False(lease.IsSafeToAct);
    }

    [Fact]
    public void IsSafeToActIsFalseAtOrBelowTheSafetyMargin()
    {
        var clock = new SteppingClock();
        var lease = Grant(clock, TimeSpan.FromSeconds(5), clock.GetTimestamp(), safetyMargin: TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.True(lease.IsSafeToAct);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(2), lease.Remaining);
        Assert.False(lease.IsSafeToAct);
    }

    [Fact]
    public void ToStringShowsNoScopeIdHashOrHolder()
    {
        var canary = ResourceId.New(ResourceTypes.Organization);
        var scope = CoordinationScope.ForResource(TestScopes.Reference(new OrganizationScope(canary)));
        var holder = Guid.NewGuid();
        var lease = Lease.Grant(scope, Name, new FencingToken(42), holder, CoordinatorKind.PostgreSql, TimeSpan.FromSeconds(5), 0, TimeSpan.FromSeconds(1), new SteppingClock());

        var text = lease.ToString();

        Assert.DoesNotContain(canary.Value, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(holder.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(holder.ToString("N"), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CoordinationKey.Hash(scope.Owner, Name.Module, Name.Name), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(canary.Value, $"{lease}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GrantKeepsItsValues()
    {
        var holder = Guid.NewGuid();
        var lease = Lease.Grant(CoordinationScope.Operator, Name, new FencingToken(7), holder, CoordinatorKind.Redis, TimeSpan.FromSeconds(5), 0, TimeSpan.FromSeconds(1), new SteppingClock());

        Assert.Same(CoordinationScope.Operator, lease.Scope);
        Assert.Equal(Name, lease.Name);
        Assert.Equal(new FencingToken(7), lease.Token);
        Assert.Equal(holder, lease.Holder);
        Assert.Equal(CoordinatorKind.Redis, lease.Issuer);
        Assert.Equal(TimeSpan.FromSeconds(1), lease.SafetyMargin);
    }

    [Fact]
    public void GrantRejectsInvalidValues()
    {
        var clock = new SteppingClock();
        var token = new FencingToken(1);
        var holder = Guid.NewGuid();
        var second = TimeSpan.FromSeconds(1);
        Action[] invalid =
        [
            () => Lease.Grant(null!, Name, token, holder, CoordinatorKind.Local, second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, null!, token, holder, CoordinatorKind.Local, second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, default, holder, CoordinatorKind.Local, second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, token, Guid.Empty, CoordinatorKind.Local, second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, token, holder, (CoordinatorKind)9, second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, token, holder, CoordinatorKind.Local, -second, 0, second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, token, holder, CoordinatorKind.Local, second, 0, -second, clock),
            () => Lease.Grant(CoordinationScope.Operator, Name, token, holder, CoordinatorKind.Local, second, 0, second, null!),
        ];

        Assert.All(invalid, create => Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(create).Code));
    }

    [Fact]
    public void AnAcquisitionIsALeaseOrContention()
    {
        var lease = Grant(new SteppingClock(), TimeSpan.FromSeconds(5), 0);

        Assert.True(LeaseAcquisition.Acquired(lease).IsAcquired);
        Assert.Same(lease, LeaseAcquisition.Acquired(lease).Lease);
        Assert.False(LeaseAcquisition.Contended.IsAcquired);
        Assert.Null(LeaseAcquisition.Contended.Lease);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => LeaseAcquisition.Acquired(null!)).Code);
    }

    private static Lease Grant(TimeProvider clock, TimeSpan granted, long sent, TimeSpan? safetyMargin = null) =>
        Lease.Grant(CoordinationScope.Operator, Name, new FencingToken(1), Guid.NewGuid(), CoordinatorKind.PostgreSql, granted, sent, safetyMargin ?? TimeSpan.FromSeconds(1), clock);

    /// <summary>A monotonic clock that moves only when the test advances it.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _timestamp = 1_000_000;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan by) => _timestamp += by.Ticks;
    }
}

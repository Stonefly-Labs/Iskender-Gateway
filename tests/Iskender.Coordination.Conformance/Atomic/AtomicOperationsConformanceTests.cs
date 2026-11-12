using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance.Atomic;

/// <summary>
/// The atomic operations conformance suite (FR-011 to FR-013, FR-029): User Story 3 scenarios 1 to 3 and the edge cases
/// of the atomic operations contract. A distributed implementation runs the concurrent scenarios from two independent
/// clients.
/// </summary>
public abstract class AtomicOperationsConformanceTests
{
    protected AtomicOperationsConformanceTests()
    {
        Module = "atomic_" + Guid.NewGuid().ToString("N")[..12];
        Name = new CoordinationName(Module, "counter");
    }

    protected abstract ICoordinatorUnderTest Coordinator { get; }

    protected string Module { get; }

    protected CoordinationName Name { get; }

    /// <summary>Extra time past an expiry, covering clock granularity on a distributed coordinator.</summary>
    protected virtual TimeSpan Slack => TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// The largest value the implementation stores exactly: the 64-bit maximum, or less where the store's numbers are
    /// narrower. A result beyond it is refused as an overflow.
    /// </summary>
    protected virtual long MaximumValue => long.MaxValue;

    /// <summary>The smallest value the implementation stores exactly, the counterpart of <see cref="MaximumValue"/>.</summary>
    protected virtual long MinimumValue => long.MinValue;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CoordinationScope Scope => Coordinator.Scopes.OrganizationA;

    private TimeSpan Expiry => Coordinator.Settings.MaximumExpiry < TimeSpan.FromMinutes(5) ? Coordinator.Settings.MaximumExpiry : TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Scenario1ConcurrentReservationsAgainstALimitGrantExactlyTheLimit()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(index => Task.Run(
            async () => await (index % 2 == 0 ? a : b).Atomic.ReserveAsync(Scope, Name, 1, 100, Expiry, Token),
            Token)));

        Assert.Equal(100, results.Count(result => result.Granted));
        Assert.Equal(100, results.Count(result => !result.Granted));
        Assert.All(results.Where(result => !result.Granted), result => Assert.Equal(100, result.Value));
        Assert.Equal(100, (await a.Atomic.ReadAsync(Scope, Name, Token))!.Value);
    }

    [Fact]
    public async Task Scenario2ReleaseLowersTheValueAndFreesCapacity()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        Assert.True((await atomic.ReserveAsync(Scope, Name, 100, 100, Expiry, Token)).Granted);
        Assert.False((await atomic.ReserveAsync(Scope, Name, 10, 100, Expiry, Token)).Granted);

        var released = await atomic.ReleaseAsync(Scope, Name, 10, Token);

        Assert.Equal(90, released!.Value);
        var reserved = await atomic.ReserveAsync(Scope, Name, 10, 100, Expiry, Token);
        Assert.True(reserved.Granted);
        Assert.Equal(100, reserved.Value);
    }

    [Fact]
    public async Task Scenario3ConcurrentCompareAndSetWithOneVersionSucceedsExactlyOnce()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        await a.Atomic.AddAsync(Scope, Name, 1, Expiry, Token);
        var version = (await a.Atomic.ReadAsync(Scope, Name, Token))!.Version;

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(index => Task.Run(
            async () => await (index % 2 == 0 ? a : b).Atomic.CompareAndSetAsync(Scope, Name, version, 1000 + index, Expiry, Token),
            Token)));

        Assert.Single(results, succeeded => succeeded);
        var stored = (await b.Atomic.ReadAsync(Scope, Name, Token))!;
        Assert.Equal(version + 1, stored.Version);
        Assert.InRange(stored.Value, 1000, 1049);
    }

    [Fact]
    public async Task AKeyIsCreatedWithItsExpiryAndLaterOperationsKeepIt()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        var shortExpiry = TimeSpan.FromMilliseconds(600);
        await atomic.AddAsync(Scope, Name, 1, shortExpiry, Token);

        await Coordinator.PassTimeAsync(TimeSpan.FromMilliseconds(300));
        await atomic.AddAsync(Scope, Name, 1, Expiry, Token);
        await atomic.ReserveAsync(Scope, Name, 1, 100, Expiry, Token);
        Assert.Equal(3, (await atomic.ReadAsync(Scope, Name, Token))!.Value);

        await Coordinator.PassTimeAsync(TimeSpan.FromMilliseconds(300) + Slack);
        Assert.Null(await atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task AnExpiredKeyCountsAsAbsentAndIsCreatedAgain()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        await atomic.AddAsync(Scope, Name, 5, TimeSpan.FromMilliseconds(300), Token);
        await Coordinator.PassTimeAsync(TimeSpan.FromMilliseconds(300) + Slack);

        Assert.Null(await atomic.ReadAsync(Scope, Name, Token));
        Assert.Null(await atomic.ReleaseAsync(Scope, Name, 1, Token));
        Assert.Equal(new CoordinationValue(2, 1), await atomic.AddAsync(Scope, Name, 2, Expiry, Token));
    }

    [Fact]
    public async Task EveryChangeRaisesTheVersionByOne()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;

        Assert.Equal(new CoordinationValue(5, 1), await atomic.AddAsync(Scope, Name, 5, Expiry, Token));
        Assert.Equal(new CoordinationValue(2, 2), await atomic.AddAsync(Scope, Name, -3, Expiry, Token));
        Assert.Equal(new Reservation(true, 4), await atomic.ReserveAsync(Scope, Name, 2, 10, Expiry, Token));
        Assert.Equal(3, (await atomic.ReadAsync(Scope, Name, Token))!.Version);
        Assert.Equal(new Reservation(false, 4), await atomic.ReserveAsync(Scope, Name, 7, 10, Expiry, Token));
        Assert.Equal(3, (await atomic.ReadAsync(Scope, Name, Token))!.Version);
        Assert.Equal(new CoordinationValue(1, 4), await atomic.ReleaseAsync(Scope, Name, 3, Token));
        Assert.True(await atomic.CompareAndSetAsync(Scope, Name, 4, 42, Expiry, Token));
        Assert.False(await atomic.CompareAndSetAsync(Scope, Name, 4, 43, Expiry, Token));
        Assert.Equal(new CoordinationValue(42, 5), await atomic.ReadAsync(Scope, Name, Token));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, -1)]
    public async Task AReservationNeedsAPositiveAmountAndANonNegativeLimit(long amount, long limit)
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(Scope, Name, amount, limit, Expiry, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Null(await atomic.ReadAsync(Scope, Name, Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AReleaseNeedsAPositiveAmount(long amount)
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        await atomic.AddAsync(Scope, Name, 5, Expiry, Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReleaseAsync(Scope, Name, amount, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(new CoordinationValue(5, 1), await atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task AReservationThatWouldOverflowIsDeniedNeverWrapped()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        await atomic.AddAsync(Scope, Name, MaximumValue, Expiry, Token);

        var reservation = await atomic.ReserveAsync(Scope, Name, 1, long.MaxValue, Expiry, Token);

        Assert.False(reservation.Granted);
        Assert.Equal(MaximumValue, reservation.Value);
        Assert.Equal(new CoordinationValue(MaximumValue, 1), await atomic.ReadAsync(Scope, Name, Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task AnAddThatWouldOverflowFailsAndChangesNothing(int sign)
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        var edge = sign > 0 ? MaximumValue : MinimumValue;
        await atomic.AddAsync(Scope, Name, edge, Expiry, Token);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await atomic.AddAsync(Scope, Name, sign, Expiry, Token));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(new CoordinationValue(edge, 1), await atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task ReleaseNeverGoesBelowZeroAndLeavesAnAbsentKeyAbsent()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        var absent = new CoordinationName(Module, "absent");

        Assert.Null(await atomic.ReleaseAsync(Scope, absent, 5, Token));
        Assert.Null(await atomic.ReadAsync(Scope, absent, Token));

        await atomic.AddAsync(Scope, Name, 5, Expiry, Token);
        Assert.Equal(new CoordinationValue(0, 2), await atomic.ReleaseAsync(Scope, Name, 10, Token));
    }

    [Fact]
    public async Task CompareAndSetWithVersionZeroCreatesOnlyAnAbsentKey()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;

        Assert.True(await atomic.CompareAndSetAsync(Scope, Name, 0, 7, Expiry, Token));
        Assert.False(await atomic.CompareAndSetAsync(Scope, Name, 0, 8, Expiry, Token));

        Assert.Equal(new CoordinationValue(7, 1), await atomic.ReadAsync(Scope, Name, Token));
        Assert.False(await atomic.CompareAndSetAsync(Scope, new CoordinationName(Module, "absent"), 3, 1, Expiry, Token));
        Assert.Null(await atomic.ReadAsync(Scope, new CoordinationName(Module, "absent"), Token));
    }

    [Fact]
    public async Task ReadingAnAbsentKeyReturnsNull()
    {
        Coordinator.Require();

        Assert.Null(await Coordinator.CreateClient(0).Atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task AnInvalidExpiryIsRefusedBeforeAnyChange()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        var beyond = Coordinator.Settings.MaximumExpiry + TimeSpan.FromSeconds(1);

        foreach (var expiry in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), beyond })
        {
            await AssertInvalidAsync(() => atomic.AddAsync(Scope, Name, 1, expiry, Token));
            await AssertInvalidAsync(() => atomic.ReserveAsync(Scope, Name, 1, 10, expiry, Token));
            await AssertInvalidAsync(() => atomic.CompareAndSetAsync(Scope, Name, 0, 1, expiry, Token));
        }

        Assert.Null(await atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task APassedDeadlineFailsWithTimeoutBeforeAnyChange()
    {
        Coordinator.Require();
        var atomic = Coordinator.CreateClient(0).Atomic;
        var expired = Coordinator.Scopes.WithDeadline(DateTimeOffset.UnixEpoch);

        await AssertTimeoutAsync(() => atomic.AddAsync(expired, Name, 1, Expiry, Token));
        await AssertTimeoutAsync(() => atomic.ReserveAsync(expired, Name, 1, 10, Expiry, Token));
        await AssertTimeoutAsync(() => atomic.ReleaseAsync(expired, Name, 1, Token));
        await AssertTimeoutAsync(() => atomic.CompareAndSetAsync(expired, Name, 0, 1, Expiry, Token));
        await AssertTimeoutAsync(() => atomic.ReadAsync(expired, Name, Token));

        Assert.Null(await atomic.ReadAsync(Scope, Name, Token));
    }

    [Fact]
    public async Task EachScopeHasItsOwnCounter()
    {
        Coordinator.Require();
        var (a, b) = (Coordinator.CreateClient(0), Coordinator.CreateClient(1));
        var scopes = Coordinator.Scopes.All;

        for (var index = 0; index < scopes.Count; index++)
        {
            await (index % 2 == 0 ? a : b).Atomic.AddAsync(scopes[index], Name, index + 1, Expiry, Token);
        }

        for (var index = 0; index < scopes.Count; index++)
        {
            Assert.Equal(new CoordinationValue(index + 1, 1), await a.Atomic.ReadAsync(scopes[index], Name, Token));
        }
    }

    private static async Task AssertInvalidAsync<T>(Func<ValueTask<T>> call) =>
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await call())).Code);

    private static async Task AssertTimeoutAsync<T>(Func<ValueTask<T>> call)
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await call());
        Assert.Same(ErrorCodes.CoordinationTimeout, failure.Code);
        Assert.Equal(CoordinationFailureClass.Timeout, CoordinationFailures.ClassOf(failure));
    }
}

using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Atomic;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using Iskender.Infrastructure.Redis.Tests.Leases;

namespace Iskender.Infrastructure.Redis.Tests.Atomic;

/// <summary>The atomic operations suite against <c>RedisAtomicOperations</c>, each client on a multiplexer of its own.</summary>
public sealed class RedisAtomicConformanceTests(RedisFixture fixture) : AtomicOperationsConformanceTests, IAsyncDisposable
{
    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);

    protected override ICoordinatorUnderTest Coordinator => _coordinator;

    // Lua numbers are doubles, so values are exact only within 2^53 - 1.
    protected override long MaximumValue => RedisScripts.MaximumMagnitude;

    protected override long MinimumValue => -RedisScripts.MaximumMagnitude;

    [Fact]
    public void ItDeclaresLeased() =>
        Assert.Equal(ConsistencyLevel.Leased, RedisAtomicOperations.Capabilities.LevelOf(CoordinationPrimitive.AtomicOperations));

    [Fact]
    public async Task AValueBeyondTheExactRangeIsRefusedBeforeAnyChange()
    {
        fixture.RequireRedis();
        var atomic = _coordinator.CreateClient(0).Atomic;
        var name = new CoordinationName(Module, "beyond");
        var scope = _coordinator.Scopes.OrganizationA;
        var beyond = RedisScripts.MaximumMagnitude + 1;
        var token = TestContext.Current.CancellationToken;

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await atomic.AddAsync(scope, name, beyond, TimeSpan.FromMinutes(1), token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await atomic.ReserveAsync(scope, name, beyond, long.MaxValue, TimeSpan.FromMinutes(1), token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await atomic.CompareAndSetAsync(scope, name, 0, -beyond, TimeSpan.FromMinutes(1), token))).Code);
        Assert.Null(await atomic.ReadAsync(scope, name, token));
    }

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}

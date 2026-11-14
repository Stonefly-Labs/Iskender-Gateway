using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Leases;

public class LocalLeaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItDeclaresLocalForEveryPrimitiveAndDoesNotOfferFencing()
    {
        Assert.Equal(CoordinatorKind.Local, LocalLeaseCoordinator.Capabilities.Kind);
        Assert.All(
            Enum.GetValues<CoordinationPrimitive>().Where(primitive => primitive != CoordinationPrimitive.Fencing),
            primitive => Assert.Equal(ConsistencyLevel.Local, LocalLeaseCoordinator.Capabilities.LevelOf(primitive)));
        Assert.Null(LocalLeaseCoordinator.Capabilities.LevelOf(CoordinationPrimitive.Fencing));
    }

    [Fact]
    public async Task NoResultEventOrDescriptionCallsItDistributed()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var leases = coordinator.Leases;
        var name = new CoordinationName("fixture", "local_" + Guid.NewGuid().ToString("N")[..8]);
        var scope = coordinator.Scopes.OrganizationA;

        var lease = (await leases.AcquireAsync(scope, name, TimeSpan.FromSeconds(1), LeaseWait.TryOnly, Token)).Lease!;
        var contended = await leases.AcquireAsync(scope, name, TimeSpan.FromSeconds(1), LeaseWait.TryOnly, Token);
        coordinator.Clock.Advance(TimeSpan.FromSeconds(2));
        var lost = await Assert.ThrowsAsync<GatewayException>(async () => await leases.RenewAsync(lease, TimeSpan.FromSeconds(1), Token));

        var described = new[] { lease.ToString(), contended.ToString()!, lost.Message, leases.ToString()! }
            .Concat(coordinator.Logger.Events.Select(logged => logged.Message))
            .Concat(coordinator.Logger.Events.SelectMany(logged => logged.Fields).Select(field => field.Value?.ToString() ?? ""));
        Assert.All(described, text => Assert.DoesNotContain("distributed", text, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CoordinatorKind.Local, lease.Issuer);
        Assert.Contains("local", lease.ToString(), StringComparison.Ordinal);
        Assert.Contains(coordinator.Logger.Events, logged => logged.Id.Id == 5005);
    }

    [Fact]
    public async Task TokensIncreaseStrictlyAcrossKeys()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var tokens = new List<long>();

        for (var index = 0; index < 10; index++)
        {
            tokens.Add(await AcquireAsync(coordinator, $"key_{index}"));
        }

        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    /// <summary>Each coordinator, one per container, has its own counter, so no state is shared between containers (ISK-0006 FR-008).</summary>
    [Fact]
    public async Task EachCoordinatorCountsItsOwnTokens()
    {
        var first = new LocalCoordinatorUnderTest();
        var second = new LocalCoordinatorUnderTest();

        var one = await AcquireAsync(first, "key_first");
        var two = await AcquireAsync(first, "key_second");

        Assert.Equal(one, await AcquireAsync(second, "key_first"));
        Assert.Equal(two, await AcquireAsync(second, "key_second"));
        Assert.True(two > one);
    }

    [Fact]
    public async Task ALeaseFromAnotherCoordinatorIsRefused()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var foreign = Lease.Grant(coordinator.Scopes.OrganizationA, new CoordinationName("fixture", "foreign"), new FencingToken(1), Guid.NewGuid(), CoordinatorKind.Redis, TimeSpan.FromSeconds(1), 0, TimeSpan.Zero, coordinator.Clock);

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Leases.RenewAsync(foreign, TimeSpan.FromSeconds(1), Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Leases.ReleaseAsync(foreign, Token))).Code);
    }

    private static async Task<long> AcquireAsync(LocalCoordinatorUnderTest coordinator, string key) =>
        (await coordinator.Leases.AcquireAsync(coordinator.Scopes.OrganizationA, new CoordinationName("fixture", key), TimeSpan.FromSeconds(1), LeaseWait.TryOnly, Token)).Lease!.Token.Value;
}

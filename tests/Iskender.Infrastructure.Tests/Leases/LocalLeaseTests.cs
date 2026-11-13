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
    public async Task TokensIncreaseStrictlyAcrossKeysAndCoordinators()
    {
        var first = new LocalCoordinatorUnderTest();
        var second = new LocalCoordinatorUnderTest();
        var tokens = new List<long>();

        for (var index = 0; index < 10; index++)
        {
            var coordinator = index % 2 == 0 ? first : second;
            var name = new CoordinationName("fixture", $"key_{index}");
            tokens.Add((await coordinator.Leases.AcquireAsync(coordinator.Scopes.OrganizationA, name, TimeSpan.FromSeconds(1), LeaseWait.TryOnly, Token)).Lease!.Token.Value);
        }

        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public async Task ALeaseFromAnotherCoordinatorIsRefused()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var foreign = Lease.Grant(coordinator.Scopes.OrganizationA, new CoordinationName("fixture", "foreign"), new FencingToken(1), Guid.NewGuid(), CoordinatorKind.Redis, TimeSpan.FromSeconds(1), 0, TimeSpan.Zero, coordinator.Clock);

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Leases.RenewAsync(foreign, TimeSpan.FromSeconds(1), Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await coordinator.Leases.ReleaseAsync(foreign, Token))).Code);
    }
}

using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis.Tests.Leases;

public sealed partial class RedisLeaseTests(RedisFixture fixture) : IAsyncDisposable
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly RedisCoordinatorUnderTest _coordinator = new(fixture);
    private readonly string _module = "rl_" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ItDeclaresLeasedLeasesAndBestEffortFencing()
    {
        Assert.Equal(CoordinatorKind.Redis, RedisLeaseCoordinator.Capabilities.Kind);
        Assert.Equal(ConsistencyLevel.Leased, RedisLeaseCoordinator.Capabilities.LevelOf(CoordinationPrimitive.Leases));
        Assert.Equal(ConsistencyLevel.BestEffort, RedisLeaseCoordinator.Capabilities.LevelOf(CoordinationPrimitive.Fencing));
    }

    [Fact]
    public async Task TheLeaseAndTokenKeysShareOneHashTagAndMatchTheKeyPattern()
    {
        fixture.RequireRedis();
        var name = new CoordinationName(_module, "lease");
        var lease = (await _coordinator.Leases(0).AcquireAsync(_coordinator.Scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;

        var keys = await KeysAsync();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.True(RedisScripts.IsAdapterKey(key), key));
        Assert.Single(keys.Select(key => HashTag().Match(key).Value).Distinct());
        Assert.Equal(["l", "t"], keys.Select(key => key[(key.LastIndexOf(':') + 1)..]).Order());

        var admin = await fixture.AdminDatabaseAsync();
        var stored = (string?)await admin.StringGetAsync(keys.Single(key => key.EndsWith(":l", StringComparison.Ordinal)));
        Assert.Equal(lease.Holder.ToString("N") + ":" + lease.Token.Value, stored);
        await _coordinator.Leases(0).ReleaseAsync(lease, Token);
    }

    [Fact]
    public async Task TheKeySpaceHoldsNoOrganizationProjectOrNameText()
    {
        fixture.RequireRedis();
        var scopes = _coordinator.Scopes;
        var leaseName = "lease_name_" + Guid.NewGuid().ToString("N")[..8];
        foreach (var scope in new[] { scopes.OrganizationA, scopes.ProjectA })
        {
            await _coordinator.Leases(0).AcquireAsync(scope, new CoordinationName(_module, leaseName), Duration, LeaseWait.TryOnly, Token);
        }

        var keys = await KeysAsync();
        Assert.Equal(4, keys.Count);
        var forbidden = new[]
        {
            scopes.OrganizationAScope.Organization.ToString(),
            scopes.ProjectAScope.Organization.ToString(),
            scopes.ProjectAScope.Project.ToString(),
            leaseName,
        };
        Assert.All(keys, key => Assert.All(forbidden, text => Assert.DoesNotContain(text, key, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task TokensIncreaseStrictlyOnOneKeyAcrossClients()
    {
        fixture.RequireRedis();
        var name = new CoordinationName(_module, "lease");
        var tokens = new List<long>();
        for (var index = 0; index < 6; index++)
        {
            var leases = _coordinator.Leases(index % 2);
            var lease = (await leases.AcquireAsync(_coordinator.Scopes.OrganizationA, name, Duration, LeaseWait.TryOnly, Token)).Lease!;
            tokens.Add(lease.Token.Value);
            await leases.ReleaseAsync(lease, Token);
        }

        Assert.Equal(tokens.Order(), tokens);
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public async Task ALeaseFromAnotherCoordinatorIsRefused()
    {
        var foreign = Lease.Grant(_coordinator.Scopes.OrganizationA, new CoordinationName(_module, "foreign"), new FencingToken(1), Guid.NewGuid(), CoordinatorKind.PostgreSql, Duration, TimeProvider.System.GetTimestamp(), TimeSpan.Zero, TimeProvider.System);
        fixture.RequireRedis();
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await _coordinator.Leases(0).RenewAsync(foreign, Duration, Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () => await _coordinator.Leases(0).ReleaseAsync(foreign, Token))).Code);
    }

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();

    private async Task<List<string>> KeysAsync()
    {
        var server = await fixture.AdminAsync();
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: $"isk:v1:{_module}:*"))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

    [GeneratedRegex(@"\{[0-9a-f]{64}\}", RegexOptions.CultureInvariant)]
    private static partial Regex HashTag();
}

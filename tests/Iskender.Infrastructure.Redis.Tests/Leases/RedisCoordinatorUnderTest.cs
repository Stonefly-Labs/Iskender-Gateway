using System.Collections.Concurrent;
using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Leases;

/// <summary>
/// The Redis coordinator under the conformance suites: each client index has a connection, and so a multiplexer, of its
/// own, standing in for a replica. Time passes in real time on the server. Events go to the conformance suite's
/// capturing logger.
/// </summary>
internal sealed class RedisCoordinatorUnderTest(RedisFixture fixture) : ICoordinatorUnderTest, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<RedisConnection>> _connections = new();

    public CoordinationSettings CoordinationSettings { get; } = fixture.UnavailableReason is null
        ? TestCoordinationSettings.For(fixture, sectionOverrides: new Dictionary<string, object>
        {
            ["leases"] = new Dictionary<string, object>
            {
                ["coordinator"] = "redis",
                ["minimumDuration"] = "300ms",
                ["maximumDuration"] = "1m",
                ["safetyMargin"] = "100ms",
                ["maximumWait"] = "5s",
            },
        })
        : null!;

    public CapturingDiagnostics.Logger Logger { get; } = new();

    public CoordinatorKind Kind => CoordinatorKind.Redis;

    public CoordinatorCapabilities Capabilities => RedisLeaseCoordinator.Capabilities;

    public bool IsDistributed => true;

    public ConformanceSettings Settings => new(
        CoordinationSettings.MinimumLeaseDuration,
        CoordinationSettings.MaximumLeaseDuration,
        CoordinationSettings.SafetyMargin,
        CoordinationSettings.MaximumWait,
        CoordinationSettings.MaximumExpiry,
        CoordinationSettings.MaximumValueBytes,
        CoordinationSettings.OperationTimeout);

    public ConformanceScopes Scopes { get; } = new();

    public void Require() => fixture.RequireRedis();

    public CoordinatorClient CreateClient(int index) => new(null!, Leases(index), null!);

    /// <summary>Client <paramref name="index"/>'s lease coordinator, on its own connection.</summary>
    public RedisLeaseCoordinator Leases(int index) => new(Connection(index));

    /// <summary>Client <paramref name="index"/>'s connection.</summary>
    public RedisConnection Connection(int index) =>
        _connections.GetOrAdd(index, _ => new Lazy<RedisConnection>(() => new RedisConnection(CoordinationSettings, TestCoordinationCredentialProvider.For(fixture), Logger))).Value;

    public async ValueTask PassTimeAsync(TimeSpan duration) => await Task.Delay(duration, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values.Where(connection => connection.IsValueCreated))
        {
            await connection.Value.DisposeAsync();
        }
    }
}

using System.Collections.Concurrent;
using Iskender.Coordination.Conformance;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.Redis.Tests.Fixture;

namespace Iskender.Infrastructure.Redis.Tests.Leases;

/// <summary>
/// The Redis coordinator under the conformance suites: each client index has a connection, and so a multiplexer, of its
/// own, standing in for a replica. Time passes in real time on the server. Events go to the conformance suite's
/// capturing logger. Given a fixture it <paramref name="ownsFixture"/>, it supports faults, stopping and pausing that
/// fixture's container, never the shared one.
/// </summary>
internal sealed class RedisCoordinatorUnderTest(RedisFixture fixture, bool ownsFixture = false) : ICoordinatorUnderTest, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<RedisConnection>> _connections = new();
    private bool _paused;

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

    public CoordinatorClient CreateClient(int index) => new(new RedisAtomicOperations(Connection(index)), Leases(index), new RedisVersionedCache(Connection(index)));

    /// <summary>Client <paramref name="index"/>'s lease coordinator, on its own connection.</summary>
    public RedisLeaseCoordinator Leases(int index) => new(Connection(index));

    /// <summary>Client <paramref name="index"/>'s connection.</summary>
    public RedisConnection Connection(int index) =>
        _connections.GetOrAdd(index, _ => new Lazy<RedisConnection>(() => new RedisConnection(CoordinationSettings, TestCoordinationCredentialProvider.For(fixture), Logger))).Value;

    public async ValueTask PassTimeAsync(TimeSpan duration) => await Task.Delay(duration, CancellationToken.None);

    public bool SupportsFaults => ownsFixture;

    public async ValueTask StopAsync()
    {
        RequireFaults();
        await fixture.StopAsync();
    }

    public async ValueTask PauseAsync()
    {
        RequireFaults();
        await fixture.PauseAsync();
        _paused = true;
    }

    public async ValueTask ResumeAsync()
    {
        RequireFaults();
        if (_paused)
        {
            await fixture.UnpauseAsync();
            _paused = false;
        }
        else
        {
            await fixture.StartAsync();
        }
    }

    private void RequireFaults()
    {
        if (!ownsFixture)
        {
            throw new NotSupportedException("Only a coordinator under test that owns its fixture supports faults.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values.Where(connection => connection.IsValueCreated))
        {
            await connection.Value.DisposeAsync();
        }
    }
}

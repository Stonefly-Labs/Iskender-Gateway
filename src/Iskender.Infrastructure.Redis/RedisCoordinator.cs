using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// The Redis adapter's public entry point (TR-2, FR-002): builds the atomic operations, leases, versioned cache, and
/// health probe over one shared <see cref="RedisConnection"/>, so a host outside this assembly, ISK-0007 first, can compose
/// the adapter without changing it. The connection is built lazily on the first call and resolves its password from
/// <paramref name="credentials"/>; disposing this disposes it, and every port then fails. Owned by ISK-0005.
/// </summary>
public sealed class RedisCoordinator : IAsyncDisposable
{
    private readonly RedisConnection _connection;

    public RedisCoordinator(CoordinationSettings settings, ICoordinationCredentialProvider? credentials, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _connection = new RedisConnection(settings ?? throw new GatewayException(ErrorCodes.ValueInvalid), credentials, logger, timeProvider);
        Atomic = new RedisAtomicOperations(_connection, timeProvider);
        Leases = new RedisLeaseCoordinator(_connection, timeProvider);
        Cache = new RedisVersionedCache(_connection, timeProvider);
        Health = new RedisHealthProbe(_connection, timeProvider);
    }

    /// <summary>What this coordinator declares: <c>leased</c> for leases, and fencing not offered.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Redis;

    public RedisAtomicOperations Atomic { get; }

    public RedisLeaseCoordinator Leases { get; }

    public RedisVersionedCache Cache { get; }

    public RedisHealthProbe Health { get; }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

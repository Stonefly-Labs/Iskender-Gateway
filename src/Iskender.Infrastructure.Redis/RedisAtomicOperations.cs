using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// Atomic operations on Redis (FR-011 to FR-013, research R9 and R10). Each operation is one script on the value hash
/// <c>{v, ver}</c>, which sets the key's expiry when it creates it. Lua numbers are doubles, so a delta, amount, or value
/// beyond <see cref="RedisScripts.MaximumMagnitude"/> fails with <c>core.value.invalid</c> before any I/O, a limit beyond
/// it means no limit below that range, and a result beyond it is refused as an overflow. A failover can lose a write, so it
/// declares <c>leased</c>. Owned by ISK-0005.
/// </summary>
public sealed class RedisAtomicOperations : IAtomicOperations
{
    private readonly RedisConnection _connection;
    private readonly TimeProvider _timeProvider;

    internal RedisAtomicOperations(RedisConnection connection, TimeProvider? timeProvider = null)
    {
        _connection = connection ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>What this coordinator declares: <c>leased</c> for atomic operations.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Redis;

    private CoordinationSettings Settings => _connection.Settings;

    public async ValueTask<CoordinationValue> AddAsync(CoordinationScope scope, CoordinationName name, long delta, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        RequireExact(delta);
        AtomicPolicy.RequireExpiry(Settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var result = (await RunAsync(scope, name, RedisScripts.Add, [delta, Milliseconds(expiry)], cancellationToken))!;
        return (long)result[0] == 1 ? new CoordinationValue((long)result[1], (long)result[2]) : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public async ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        RequireExact(amount);
        AtomicPolicy.RequireLimit(limit);
        AtomicPolicy.RequireExpiry(Settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var result = (await RunAsync(scope, name, RedisScripts.Reserve, [amount, Math.Min(limit, RedisScripts.MaximumMagnitude), Milliseconds(expiry)], cancellationToken))!;
        return new Reservation((long)result[0] == 1, (long)result[1]);
    }

    public async ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        RequireExact(amount);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var result = await RunAsync(scope, name, RedisScripts.Release, [amount], cancellationToken);
        return result is null ? null : new CoordinationValue((long)result[0], (long)result[1]);
    }

    public async ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        RequireExact(value);
        AtomicPolicy.RequireExpiry(Settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var result = await _connection.RunAsync(scope, name.Module, CoordinationPrimitive.AtomicOperations, RedisScripts.CompareAndSet, [RedisScripts.ValueKey(scope, name)], [expectedVersion, value, Milliseconds(expiry)], cancellationToken);
        return (long)result == 1;
    }

    public async ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var result = await RunAsync(scope, name, RedisScripts.Read, [], cancellationToken);
        return result is null ? null : new CoordinationValue((long)result[0], (long)result[1]);
    }

    // A script's array reply, or null for a nil reply.
    private async ValueTask<RedisResult[]?> RunAsync(CoordinationScope scope, CoordinationName name, RedisScript script, RedisValue[] arguments, CancellationToken cancellationToken)
    {
        var result = await _connection.RunAsync(scope, name.Module, CoordinationPrimitive.AtomicOperations, script, [RedisScripts.ValueKey(scope, name)], arguments, cancellationToken);
        return result.IsNull ? null : (RedisResult[])result!;
    }

    private static void RequireExact(long value)
    {
        if (value > RedisScripts.MaximumMagnitude || value < -RedisScripts.MaximumMagnitude)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    private static RedisValue Milliseconds(TimeSpan duration) => (long)Math.Ceiling(duration.TotalMilliseconds);
}

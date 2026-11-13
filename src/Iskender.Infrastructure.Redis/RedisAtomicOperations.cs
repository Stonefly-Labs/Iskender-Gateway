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
        var (applied, value) = await RunAsync(scope, name, RedisScripts.Add, [delta, Milliseconds(expiry)], FlaggedValueReply, cancellationToken);
        return applied ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public async ValueTask<Reservation> ReserveAsync(CoordinationScope scope, CoordinationName name, long amount, long limit, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        RequireExact(amount);
        AtomicPolicy.RequireLimit(limit);
        AtomicPolicy.RequireExpiry(Settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        var (granted, value) = await RunAsync(scope, name, RedisScripts.Reserve, [amount, Math.Min(limit, RedisScripts.MaximumMagnitude), Milliseconds(expiry)], FlaggedValueReply, cancellationToken);
        return new Reservation(granted, value.Value);
    }

    public async ValueTask<CoordinationValue?> ReleaseAsync(CoordinationScope scope, CoordinationName name, long amount, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        AtomicPolicy.RequireAmount(amount);
        RequireExact(amount);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return await RunAsync(scope, name, RedisScripts.Release, [amount], ValueReply, cancellationToken);
    }

    public async ValueTask<bool> CompareAndSetAsync(CoordinationScope scope, CoordinationName name, long expectedVersion, long value, TimeSpan expiry, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        RequireExact(value);
        AtomicPolicy.RequireExpiry(Settings, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return await RunAsync(scope, name, RedisScripts.CompareAndSet, [expectedVersion, value, Milliseconds(expiry)], RedisReplies.Flag, cancellationToken);
    }

    public async ValueTask<CoordinationValue?> ReadAsync(CoordinationScope scope, CoordinationName name, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return await RunAsync(scope, name, RedisScripts.Read, [], ValueReply, cancellationToken);
    }

    /// <summary>The add and reserve scripts' <c>{applied, value, version}</c>.</summary>
    internal static (bool Applied, CoordinationValue Value) FlaggedValueReply(RedisResult reply)
    {
        var elements = RedisReplies.Array(reply, 3);
        return (RedisReplies.Flag(elements[0]), new CoordinationValue(RedisReplies.Integer(elements[1]), RedisReplies.Integer(elements[2])));
    }

    /// <summary>The release and read scripts' <c>{value, version}</c>, or <c>null</c> for a nil reply.</summary>
    internal static CoordinationValue? ValueReply(RedisResult reply) =>
        RedisReplies.ArrayOrNil(reply, 2) is { } elements ? new CoordinationValue(RedisReplies.Integer(elements[0]), RedisReplies.Integer(elements[1])) : null;

    private ValueTask<T> RunAsync<T>(CoordinationScope scope, CoordinationName name, RedisScript script, RedisValue[] arguments, Func<RedisResult, T> parse, CancellationToken cancellationToken) =>
        _connection.RunAsync(scope, name.Module, CoordinationPrimitive.AtomicOperations, script, [RedisScripts.ValueKey(scope, name)], arguments, parse, cancellationToken);

    private static void RequireExact(long value)
    {
        if (value > RedisScripts.MaximumMagnitude || value < -RedisScripts.MaximumMagnitude)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    private static RedisValue Milliseconds(TimeSpan duration) => (long)Math.Ceiling(duration.TotalMilliseconds);
}

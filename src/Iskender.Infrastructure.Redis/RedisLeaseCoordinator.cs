using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// Leases on Redis (FR-014 to FR-016, research R9, R10, R12). Acquire is one script that takes an absent key with
/// <c>SET NX PX</c> and a token from <c>INCR</c> on the token key under the same hash tag; renew and release change the
/// key only while it still holds this lease's <c>holder:token</c>. Every grant returns the key's <c>PTTL</c>, and the
/// lease's remaining time is that less the time since the script was sent. A failover can lose a write, so it declares
/// <c>leased</c> for leases and <c>best_effort</c> for fencing. Owned by ISK-0005.
/// </summary>
public sealed class RedisLeaseCoordinator : ILeaseCoordinator
{
    private readonly RedisConnection _connection;
    private readonly TimeProvider _timeProvider;

    internal RedisLeaseCoordinator(RedisConnection connection, TimeProvider? timeProvider = null)
    {
        _connection = connection ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>What this coordinator declares: <c>leased</c> for leases and <c>best_effort</c> for fencing.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.Redis;

    private CoordinationSettings Settings => _connection.Settings;

    public ValueTask<LeaseAcquisition> AcquireAsync(CoordinationScope scope, CoordinationName name, TimeSpan duration, LeaseWait wait, CancellationToken cancellationToken)
    {
        LeasePolicy.RequireTarget(scope, name);
        LeasePolicy.RequireDuration(Settings, duration);
        LeasePolicy.RequireWait(Settings, wait);
        RedisKey[] keys = [RedisScripts.LeaseKey(scope, name), RedisScripts.TokenKey(scope, name)];
        return LeasePolicy.AcquireAsync(
            scope,
            wait,
            _timeProvider,
            async token =>
            {
                var holder = Guid.NewGuid();
                var sent = _timeProvider.GetTimestamp();
                var result = (RedisResult[])(await _connection.RunAsync(scope, name.Module, CoordinationPrimitive.Leases, RedisScripts.Acquire, keys, [Holder(holder), Milliseconds(duration)], token))!;
                return (long)result[0] == 0
                    ? LeaseAcquisition.Contended
                    : LeaseAcquisition.Acquired(Lease.Grant(scope, name, new FencingToken((long)result[1]), holder, CoordinatorKind.Redis, TimeSpan.FromMilliseconds((long)result[2]), sent, Settings.SafetyMargin, _timeProvider));
            },
            cancellationToken);
    }

    public async ValueTask<Lease> RenewAsync(Lease lease, TimeSpan duration, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireDuration(Settings, duration);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        var sent = _timeProvider.GetTimestamp();
        var result = (RedisResult[])(await _connection.RunAsync(lease.Scope, lease.Name.Module, CoordinationPrimitive.Leases, RedisScripts.Renew, [RedisScripts.LeaseKey(lease.Scope, lease.Name)], [Stored(lease), Milliseconds(duration)], cancellationToken))!;
        if ((long)result[0] == 0)
        {
            CoordinationLog.LogLeaseLost(_connection.Logger, lease.Name.Module, CoordinatorKind.Redis);
            CoordinationTelemetry.RecordLeaseLost(lease.Name.Module, CoordinatorKind.Redis);
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationLeaseLost, CoordinationFailureClass.LeaseLost);
        }

        return Lease.Grant(lease.Scope, lease.Name, lease.Token, lease.Holder, CoordinatorKind.Redis, TimeSpan.FromMilliseconds((long)result[1]), sent, Settings.SafetyMargin, _timeProvider);
    }

    public async ValueTask ReleaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        RequireOwn(lease);
        LeasePolicy.RequireTime(lease.Scope, _timeProvider, cancellationToken);
        await _connection.RunAsync(lease.Scope, lease.Name.Module, CoordinationPrimitive.Leases, RedisScripts.ReleaseLease, [RedisScripts.LeaseKey(lease.Scope, lease.Name)], [Stored(lease)], cancellationToken);
    }

    private static RedisValue Holder(Guid holder) => holder.ToString("N");

    // The value the lease key holds while this lease has it.
    private static RedisValue Stored(Lease lease) => lease.Holder.ToString("N") + ":" + lease.Token.Value.ToString(CultureInfo.InvariantCulture);

    private static RedisValue Milliseconds(TimeSpan duration) => (long)Math.Ceiling(duration.TotalMilliseconds);

    // A lease from another coordinator has no key here to renew or release.
    private static void RequireOwn(Lease lease)
    {
        if (lease is null || lease.Issuer != CoordinatorKind.Redis)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }
}

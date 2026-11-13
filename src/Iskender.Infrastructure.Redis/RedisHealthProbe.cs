using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// Redis's part of the coordination health probe (FR-025, research R17): <c>PING</c>, then <c>ROLE</c>, where any role
/// but primary gives <c>read_only</c>. It is bounded by <c>healthProbeTimeout</c>, and a coordinator condition is a
/// reason, never an exception; only the caller's token throws, with <c>coordination.cancelled</c>. It shares the
/// adapter's connection, so a refused credential is resolved again on the next check. Owned by ISK-0005.
/// </summary>
public sealed class RedisHealthProbe : ICoordinationHealthProbe
{
    private readonly RedisConnection _connection;
    private readonly TimeProvider _timeProvider;

    internal RedisHealthProbe(RedisConnection connection, TimeProvider? timeProvider = null)
    {
        _connection = connection ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<CoordinationHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        CoordinationHealthProbe.RequireNotCancelled(cancellationToken);
        using var bound = new CancellationTokenSource(_connection.Settings.HealthProbeTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);
        IDatabase? database = null;
        try
        {
            database = await _connection.DatabaseAsync(linked.Token).AsTask().WaitAsync(linked.Token);
            await database.PingAsync().WaitAsync(linked.Token);
            var role = await database.ExecuteAsync("ROLE").WaitAsync(linked.Token);
            return IsPrimary(role) ? CoordinationHealthResult.Healthy : CoordinationHealthResult.Unhealthy(CoordinationHealthReason.ReadOnly);
        }
        catch (Exception caught) when (caught is not GatewayException)
        {
            CoordinationHealthProbe.RequireNotCancelled(cancellationToken);
            if (bound.IsCancellationRequested)
            {
                return CoordinationHealthResult.Unhealthy(CoordinationHealthReason.Timeout);
            }

            var (_, failureClass, _) = RedisFailureClassifier.Map(await _connection.ExplainAsync(caught, database?.Multiplexer, linked.Token), RedisStop.None, commandSent: false);
            return CoordinationHealthProbe.ResultFor(failureClass, CoordinatorKind.Redis, _connection.Logger);
        }
    }

    // ROLE's first element is "master" on a primary, and "slave" or "sentinel" otherwise. Any other reply is unexpected.
    private static bool IsPrimary(RedisResult role)
    {
        if (role.Resp2Type != ResultType.Array || (RedisResult[])role! is not [var kind, ..])
        {
            throw new InvalidOperationException("The server returned an unexpected role reply.");
        }

        return (string?)kind == "master";
    }
}

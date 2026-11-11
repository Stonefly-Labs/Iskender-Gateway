using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// The migration runner's session advisory lock on the published key <c>6156145740695756181</c> (contract Migrations
/// rule 8, research R12). It is taken with <c>pg_try_advisory_lock</c> polled every 250 ms, not with
/// <c>lock_timeout</c>, so the connection stays usable to report the failure. A crashed holder's lock is released with
/// its connection. The only session-scoped lock in the adapter. Owned by ISK-0004.
/// </summary>
internal sealed class MigrationLock : IAsyncDisposable
{
    public const long Key = AdvisoryLockKey.Migration;

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly NpgsqlConnection _connection;

    private MigrationLock(NpgsqlConnection connection) => _connection = connection;

    /// <summary>Polls until the lock is held, or fails with <c>persistence.migration_locked</c> once <paramref name="wait"/> passes, having changed nothing.</summary>
    public static async Task<MigrationLock> AcquireAsync(NpgsqlConnection connection, TimeSpan wait, TimeProvider timeProvider, ILogger logger, string operation, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection) { Parameters = { new() { Value = Key } } };
        var waiting = false;
        while (true)
        {
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return new MigrationLock(connection);
            }

            var waited = timeProvider.GetElapsedTime(started);
            if (!waiting)
            {
                PersistenceLog.LogMigrationLockWaiting(logger, operation, waited);
                waiting = true;
            }

            if (waited >= wait)
            {
                throw new GatewayException(ErrorCodes.PersistenceMigrationLocked);
            }

            await Task.Delay(Min(PollInterval, wait - waited), timeProvider, cancellationToken);
        }
    }

    /// <summary>Releases the lock. A broken connection has already released it, so a failure here is ignored.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", _connection) { Parameters = { new() { Value = Key } } };
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            // The server releases a session lock when its connection closes.
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}

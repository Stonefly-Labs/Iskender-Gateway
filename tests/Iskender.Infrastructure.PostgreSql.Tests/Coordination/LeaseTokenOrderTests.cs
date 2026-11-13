using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// FR-014, TR-9, contract Leases and fencing rule 2, and User Story 2 scenario 4: the tokens of one lease key are granted
/// in strictly increasing order, even when an acquire waits on the key while another holder takes, uses, and gives up a
/// later token, and its row is deleted by a release or a purge.
/// </summary>
public class LeaseTokenOrderTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(300);

    private readonly string _module = "order_" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAcquireDelayedPastAnotherGrantAndItsDeletionGetsAHigherToken()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.OrganizationA;
        var name = new CoordinationName(_module, "delayed");
        var (scopeKey, keyHash) = (CoordinationKey.ScopeKey(scope.Owner), CoordinationKey.Hash(scope.Owner, name.Module, name.Name));

        // Another holder's acquire, still committing, holds the key, so the client's acquire waits on it.
        await using var other = await coordinator.Fixture.OpenSuperuserAsync(coordinator.Database.Name);
        await using var transaction = await other.BeginTransactionAsync(Token);
        await ExecuteAsync(other, $"INSERT INTO isk_coordination.leases VALUES ('{scopeKey}', '{keyHash}', gen_random_uuid(), 0, clock_timestamp() + interval '1 minute')");
        var acquiring = coordinator.Leases(0).AcquireAsync(scope, name, TimeSpan.FromSeconds(30), LeaseWait.TryOnly, Token).AsTask();
        await WaitUntilAnAcquireWaitsOnTheKeyAsync();

        // That holder draws its token, uses it, and its row is deleted, as a release or a purge does, before it commits.
        var used = await ScalarAsync(other, $"UPDATE isk_coordination.leases SET token = nextval('isk_coordination.fencing_tokens') WHERE scope_key = '{scopeKey}' AND key_hash = '{keyHash}' RETURNING token");
        await ExecuteAsync(other, $"DELETE FROM isk_coordination.leases WHERE scope_key = '{scopeKey}' AND key_hash = '{keyHash}'");
        await transaction.CommitAsync(Token);

        var granted = await acquiring.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.True(granted.IsAcquired);
        Assert.True(granted.Lease!.Token.Value > used, $"The delayed grant's token {granted.Lease.Token.Value} is not above the {used} used before it.");
        await coordinator.Leases(0).ReleaseAsync(granted.Lease, Token);
    }

    [Fact]
    public async Task InterleavedAcquiresReleasesAndPurgesOnOneKeyGrantIncreasingTokens()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.ProjectA;
        var name = new CoordinationName(_module, "interleaved");
        var maintenance = new CoordinationMaintenance(coordinator.Units(1), coordinator.CoordinationSettings);
        var grants = new List<long>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);

        // Only the holder appends, so the list is in grant order; some grants expire unreleased and are purged.
        async Task ClientAsync(int index)
        {
            var leases = coordinator.Leases(index % 2);
            for (var round = 0; round < 12; round++)
            {
                var acquisition = await leases.AcquireAsync(scope, name, Brief, LeaseWait.Bounded(TimeSpan.FromSeconds(5)), Token);
                Assert.True(acquisition.IsAcquired);
                lock (grants)
                {
                    grants.Add(acquisition.Lease!.Token.Value);
                }

                if (round % 3 == 0)
                {
                    await Task.Delay(Brief + TimeSpan.FromMilliseconds(20), Token);
                }
                else
                {
                    await leases.ReleaseAsync(acquisition.Lease!, Token);
                }
            }
        }

        var purging = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await maintenance.PurgeExpiredAsync(scope, 100, Token);
                await Task.Delay(TimeSpan.FromMilliseconds(5), Token);
            }
        }, Token);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(ClientAsync));
        await stop.CancelAsync();
        await purging;

        Assert.Equal(48, grants.Count);
        Assert.All(grants.Zip(grants.Skip(1)), pair => Assert.True(pair.Second > pair.First, $"Token {pair.Second} was granted after {pair.First}."));
    }

    private async Task WaitUntilAnAcquireWaitsOnTheKeyAsync()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var waiting = await coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, $"SELECT count(*) FROM pg_stat_activity WHERE datname = '{coordinator.Database.Name}' AND wait_event_type = 'Lock'");
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Token);
        }

        Assert.Fail("The acquire never waited on the key.");
    }

    private static async Task ExecuteAsync(Npgsql.NpgsqlConnection connection, string sql)
    {
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<long> ScalarAsync(Npgsql.NpgsqlConnection connection, string sql)
    {
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }
}

using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The dispatch shard leases (<c>work</c> contract Dispatch and apply rules 2 and 3, research R6 steps 3 and 6): an
/// item's shard is its bucket modulo <c>dispatch.shards</c>, and a shard's lease and fence resource are both
/// <c>work</c> / <c>dispatch.&lt;shard&gt;</c> in the item's scope, so a scope has at most 64 of each. The leases come
/// only from the <c>work.dispatch</c> feature's gate. No advisory lock is used. Owned by ISK-0009.
/// </summary>
internal sealed class ShardLeases
{
    /// <summary>The lease and fence module.</summary>
    public const string Module = "work";

    /// <summary>The most shards a scope can have, and so the most lease keys and fence records.</summary>
    public const int MaximumShards = 64;

    private readonly ILeaseCoordinator _leases;
    private readonly TimeSpan _duration;
    private readonly TimeProvider _timeProvider;

    /// <param name="leases">The <c>work.dispatch</c> feature's leases, <c>CoordinationGate.ForFeature("work.dispatch").Leases</c>.</param>
    /// <param name="duration">The work section's <c>leaseDuration</c>.</param>
    public ShardLeases(ILeaseCoordinator leases, TimeSpan duration, TimeProvider? timeProvider)
    {
        _leases = leases ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _duration = duration > TimeSpan.Zero ? duration : throw new GatewayException(ErrorCodes.ValueInvalid);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The shard of an item with <paramref name="bucket"/>, under <paramref name="shards"/> shards.</summary>
    public static int ShardOf(int bucket, int shards) =>
        bucket is >= 0 and < MaximumShards && shards is >= 1 and <= MaximumShards ? bucket % shards : throw new GatewayException(ErrorCodes.ValueInvalid);

    /// <summary>The lease key and fence resource of <paramref name="shard"/>: <c>work</c> / <c>dispatch.&lt;shard&gt;</c>.</summary>
    public static CoordinationName Name(int shard) =>
        shard is >= 0 and < MaximumShards ? new(Module, "dispatch." + shard.ToString(CultureInfo.InvariantCulture)) : throw new GatewayException(ErrorCodes.ValueInvalid);

    /// <summary>
    /// Tries the shard's lease once, without waiting. Contention returns <c>null</c>, and the caller skips the shard. A
    /// granted lease is renewed every third of the lease duration until it is released.
    /// </summary>
    public async ValueTask<ShardLease?> TryAcquireAsync(UnitOfWorkScope scope, int shard, CancellationToken cancellationToken)
    {
        var acquisition = await _leases.AcquireAsync(CoordinationScope.From(scope), Name(shard), _duration, LeaseWait.TryOnly, cancellationToken);
        return acquisition.Lease is { } lease ? new ShardLease(_leases, lease, shard, _duration, _timeProvider) : null;
    }
}

/// <summary>
/// One held shard lease. It renews itself every third of the lease duration, and when a renewal fails with
/// <c>coordination.lease_lost</c>, or the lease is no longer safe to act on after a failed renewal, it cancels
/// <see cref="Lost"/>, which every in-flight handler of the shard shares. Owned by ISK-0009.
/// </summary>
internal sealed class ShardLease : IAsyncDisposable
{
    private readonly ILeaseCoordinator _leases;
    private readonly TimeSpan _duration;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lost = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _renewal;
    private Lease _current;
    private int _released;

    internal ShardLease(ILeaseCoordinator leases, Lease lease, int shard, TimeSpan duration, TimeProvider timeProvider)
        : this(leases, lease, ShardLeases.Name(shard), shard, duration, timeProvider)
    {
    }

    /// <summary>A held lease of another resource, such as a recurring job's, renewed the same way, with no shard.</summary>
    internal ShardLease(ILeaseCoordinator leases, Lease lease, CoordinationName resource, TimeSpan duration, TimeProvider timeProvider)
        : this(leases, lease, resource, shard: -1, duration, timeProvider)
    {
    }

    private ShardLease(ILeaseCoordinator leases, Lease lease, CoordinationName resource, int shard, TimeSpan duration, TimeProvider timeProvider)
    {
        _leases = leases;
        _current = lease;
        _duration = duration;
        _timeProvider = timeProvider;
        Shard = shard;
        Resource = resource;
        _renewal = RenewAsync();
    }

    /// <summary>The shard, or -1 for a lease that is not a shard's.</summary>
    public int Shard { get; }

    /// <summary>The fence resource, the same name as the lease key.</summary>
    public CoordinationName Resource { get; }

    /// <summary>The latest granted lease.</summary>
    public Lease Current => Volatile.Read(ref _current);

    /// <summary>Fires when the lease is lost. The shard's handlers stop, and nothing more is written for their items.</summary>
    public CancellationToken Lost => _lost.Token;

    public bool IsLost => _lost.IsCancellationRequested;

    /// <summary>Whether the holder may still act: the lease is not lost and is above its safety margin.</summary>
    public bool IsSafeToAct => !IsLost && Current.IsSafeToAct;

    /// <summary>Renewals made so far, for tests of the cadence.</summary>
    public int Renewals { get; private set; }

    /// <summary>Stops renewing and releases the lease. A release that fails leaves the lease to expire on its own.</summary>
    public async ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync();
        await _renewal;
        if (IsLost)
        {
            return;
        }

        try
        {
            await _leases.ReleaseAsync(Current, cancellationToken);
        }
        catch (Exception exception) when (exception is GatewayException or OperationCanceledException)
        {
            // The lease expires on its own (work contract Shutdown rule 3).
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None);
        _lost.Dispose();
        _stop.Dispose();
    }

    private async Task RenewAsync()
    {
        var period = _duration / 3;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(period, _timeProvider, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                Volatile.Write(ref _current, await _leases.RenewAsync(Current, _duration, _stop.Token));
                Renewals++;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (GatewayException failure) when (ReferenceEquals(failure.Code, ErrorCodes.CoordinationLeaseLost))
            {
                await _lost.CancelAsync();
                return;
            }
            catch (Exception) when (!Current.IsSafeToAct)
            {
                await _lost.CancelAsync();
                return;
            }
            catch (Exception)
            {
                // A renewal that fails while the lease is still safe is tried again at the next period.
            }
        }
    }
}

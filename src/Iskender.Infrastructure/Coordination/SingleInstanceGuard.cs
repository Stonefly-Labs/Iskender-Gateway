using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.Coordination;

/// <summary>The plane whose single instance a <see cref="SingleInstanceGuard"/> verifies. Owned by ISK-0005.</summary>
public enum SingleInstancePlane
{
    DataPlane,
    ControlPlane,
}

/// <summary>
/// Verifies a <c>single_instance</c> declaration (FR-007a, contract Requirements rule 6, research R14) by holding the
/// operator-scope lease <c>coordination</c> / <c>instance.data_plane</c> or <c>instance.control_plane</c>. Started, it
/// tries once to take the lease and reports <see cref="SingleInstanceState.Held"/> or
/// <see cref="SingleInstanceState.HeldByOther"/>; then, every third of the duration on its <see cref="TimeProvider"/>,
/// it renews a held lease or tries again to take one it does not hold. A failed renewal, or a lease no longer safe to
/// act on when the state is read, is <see cref="SingleInstanceState.Lost"/>, until it takes the lease again. With no
/// lease coordinator, or one whose kind is not PostgreSQL, it reports <see cref="SingleInstanceState.Unverified"/> and
/// takes nothing: a local or Redis lease cannot show that no other process holds the plane (FR-007a), and a contended one
/// would otherwise read as held by another process. A lease that is nonetheless not issued by PostgreSQL is released at
/// once and never retried. Every change writes event 5011, and disposing it
/// releases the lease. ISK-0006 starts it; this spec starts it only from tests. Owned by ISK-0005.
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceStatus, IAsyncDisposable
{
    public const string Module = "coordination";

    private readonly ILeaseCoordinator? _leases;
    private readonly CoordinatorKind _kind;
    private readonly CoordinationName _name;
    private readonly TimeSpan _duration;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private SingleInstanceState _state = SingleInstanceState.Unverified;
    private Lease? _lease;
    private ITimer? _timer;
    private bool _disposed;
    private bool _unverifiable;

    /// <param name="leases">The lease coordinator, or <c>null</c> when none is configured.</param>
    /// <param name="kind">The kind of <paramref name="leases"/>, from <see cref="CoordinationSettings.LeaseCoordinator"/>.</param>
    public SingleInstanceGuard(ILeaseCoordinator? leases, CoordinatorKind kind, SingleInstancePlane plane, TimeSpan duration, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(plane) || duration <= TimeSpan.Zero)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _leases = leases;
        _kind = kind;
        _name = NameOf(plane);
        _duration = duration;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The current state. A held lease no longer safe to act on is reported, and recorded, as lost.</summary>
    public SingleInstanceState State
    {
        get
        {
            lock (_gate)
            {
                if (_state == SingleInstanceState.Held && _lease is { IsSafeToAct: false })
                {
                    Change(SingleInstanceState.Lost, lease: null);
                }

                return _state;
            }
        }
    }

    /// <summary>How often a held lease is renewed: a third of the duration.</summary>
    public TimeSpan RenewalPeriod => _duration / 3;

    /// <summary>The lease name for <paramref name="plane"/>.</summary>
    public static CoordinationName NameOf(SingleInstancePlane plane) => plane switch
    {
        SingleInstancePlane.DataPlane => new(Module, "instance.data_plane"),
        SingleInstancePlane.ControlPlane => new(Module, "instance.control_plane"),
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>
    /// Tries once to take the lease and starts the renewal timer. Without a lease coordinator, or with one that is not
    /// PostgreSQL, it stays <see cref="SingleInstanceState.Unverified"/> and starts nothing.
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_leases is null || _kind != CoordinatorKind.PostgreSql)
        {
            return;
        }

        await TickAsync(cancellationToken);
        if (Volatile.Read(ref _unverifiable))
        {
            return;
        }

        _timer = _timeProvider.CreateTimer(_ => _ = TickAsync(_stopping.Token), state: null, RenewalPeriod, RenewalPeriod);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
        }

        await _stopping.CancelAsync();
        await _running.WaitAsync(CancellationToken.None);
        try
        {
            Lease? held;
            lock (_gate)
            {
                held = _lease;
                _lease = null;
            }

            if (held is not null && _leases is not null)
            {
                try
                {
                    await _leases.ReleaseAsync(held, CancellationToken.None);
                }
                catch (GatewayException)
                {
                    // The lease expires on its own; there is nothing more a disposing guard can do.
                }
            }
        }
        finally
        {
            _running.Release();
            _running.Dispose();
            _stopping.Dispose();
        }
    }

    // One renewal or acquisition at a time; a tick that finds the previous one still running is skipped.
    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_disposed || !await _running.WaitAsync(TimeSpan.Zero, CancellationToken.None))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            // A timer callback queued before disposing ran after it.
            return;
        }

        try
        {
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref _unverifiable))
            {
                return;
            }

            Lease? held;
            lock (_gate)
            {
                held = _state == SingleInstanceState.Held ? _lease : null;
            }

            if (held is not null)
            {
                await RenewAsync(held, cancellationToken);
            }
            else
            {
                await AcquireAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing stopped the tick.
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task RenewAsync(Lease held, CancellationToken cancellationToken)
    {
        Lease renewed;
        try
        {
            renewed = await _leases!.RenewAsync(held, _duration, cancellationToken);
        }
        catch (GatewayException) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                Change(SingleInstanceState.Lost, lease: null);
            }

            return;
        }

        lock (_gate)
        {
            if (_state == SingleInstanceState.Held)
            {
                _lease = renewed;
            }
        }
    }

    private async Task AcquireAsync(CancellationToken cancellationToken)
    {
        LeaseAcquisition acquisition;
        try
        {
            acquisition = await _leases!.AcquireAsync(CoordinationScope.Operator, _name, _duration, LeaseWait.TryOnly, cancellationToken);
        }
        catch (GatewayException) when (!cancellationToken.IsCancellationRequested)
        {
            // The coordinator cannot be reached, so exclusivity cannot be shown: a first attempt counts as lost, so the
            // gate fails single_instance features, rather than as held by another process, which the checker refuses.
            lock (_gate)
            {
                if (_state == SingleInstanceState.Unverified)
                {
                    Change(SingleInstanceState.Lost, lease: null);
                }
            }

            return;
        }

        if (acquisition.Lease is { Issuer: not CoordinatorKind.PostgreSql } unverifiable)
        {
            Volatile.Write(ref _unverifiable, true);
            lock (_gate)
            {
                Change(SingleInstanceState.Unverified, lease: null);
            }

            try
            {
                await _leases!.ReleaseAsync(unverifiable, CancellationToken.None);
            }
            catch (GatewayException)
            {
                // The lease expires on its own, and the guard never acts on it.
            }

            return;
        }

        lock (_gate)
        {
            if (acquisition.Lease is { } lease)
            {
                Change(SingleInstanceState.Held, lease);
            }
            else if (_state == SingleInstanceState.Unverified)
            {
                Change(SingleInstanceState.HeldByOther, lease: null);
            }
        }
    }

    // Called under _gate. Writes event 5011 only when the state actually changes.
    private void Change(SingleInstanceState state, Lease? lease)
    {
        var previous = _state;
        _state = state;
        _lease = lease;
        if (previous != state)
        {
            CoordinationLog.LogSingleInstanceChanged(_logger, state, previous);
        }
    }
}

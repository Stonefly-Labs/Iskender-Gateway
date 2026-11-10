namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// A unit of work's tokens (research R7): the caller's and the request's cancellation, and the unit, statement, and
/// acquire bounds, each already capped by the deadline. It tells which one fired, so a failure reads as timeout or
/// cancelled. Owned by ISK-0004.
/// </summary>
internal sealed class UnitBounds : IDisposable
{
    /// <summary>How much later than the server's <c>statement_timeout</c> the client sends its own cancel request.</summary>
    private static readonly TimeSpan ClientGrace = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _unitTimeout;
    private readonly CancellationTokenSource _unit;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationToken _request;
    private readonly CancellationToken _caller;
    private bool _timeoutFired;

    public UnitBounds(TimeSpan unit, TimeSpan statement, TimeSpan acquire, TimeProvider timeProvider, CancellationToken request, CancellationToken caller)
    {
        Statement = statement;
        AcquireBound = acquire;
        _request = request;
        _caller = caller;
        _timeProvider = timeProvider;
        _unitTimeout = new CancellationTokenSource(unit, timeProvider);
        _unit = CancellationTokenSource.CreateLinkedTokenSource(_unitTimeout.Token, request, caller);
    }

    /// <summary>The statement bound, the value of <c>statement_timeout</c>.</summary>
    public TimeSpan Statement { get; }

    public TimeSpan AcquireBound { get; }

    /// <summary>Fires when the unit's bound passes or the caller or request is cancelled.</summary>
    public CancellationToken Unit => _unit.Token;

    /// <summary>A token for acquiring and opening, which also fires at the acquire bound.</summary>
    public Scoped Acquire() => Scoped.Create(this, AcquireBound);

    /// <summary>A token for one statement, which also fires a little after <see cref="Statement"/>, and at the caller's token.</summary>
    public Scoped ForStatement(CancellationToken caller) => Scoped.Create(this, Statement + ClientGrace, caller);

    /// <summary>Cancels every operation of the unit, as when it is disposed.</summary>
    public void CancelAll()
    {
        try
        {
            _unit.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Which token fired: cancellation wins over a timeout, since the caller stopped waiting.</summary>
    public FiredToken Fired(Scoped? scoped = null, CancellationToken caller = default) =>
        _caller.IsCancellationRequested || _request.IsCancellationRequested || caller.IsCancellationRequested ? FiredToken.Cancellation
        : _timeoutFired || _unitTimeout.IsCancellationRequested || scoped?.TimedOut == true ? FiredToken.Timeout
        : FiredToken.None;

    public void Dispose()
    {
        _unit.Dispose();
        _unitTimeout.Dispose();
    }

    /// <summary>A short-lived token linked to the unit, with its own timeout.</summary>
    internal sealed class Scoped : IDisposable
    {
        private readonly UnitBounds _owner;
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _linked;

        private Scoped(UnitBounds owner, CancellationTokenSource timeout, CancellationTokenSource linked)
        {
            _owner = owner;
            _timeout = timeout;
            _linked = linked;
        }

        public CancellationToken Token => _linked.Token;

        public bool TimedOut => _timeout.IsCancellationRequested;

        public static Scoped Create(UnitBounds owner, TimeSpan bound, CancellationToken extra = default)
        {
            var timeout = new CancellationTokenSource(bound, owner._timeProvider);
            return new Scoped(owner, timeout, CancellationTokenSource.CreateLinkedTokenSource(owner.Unit, timeout.Token, extra));
        }

        public void Dispose()
        {
            if (_timeout.IsCancellationRequested)
            {
                _owner._timeoutFired = true;
            }

            _linked.Dispose();
            _timeout.Dispose();
        }
    }
}

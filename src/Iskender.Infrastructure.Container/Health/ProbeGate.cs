namespace Iskender.Infrastructure.Container.Health;

/// <summary>
/// Runs one probe at most once at a time, shares the call with every caller that arrives while it runs, and reuses a
/// result no older than the cache duration on the container's clock (container contract, Readiness rule 8; research R11).
/// The call runs on its own token, bounded by <c>bound</c>, so a caller that hangs up never cancels it; each caller waits
/// with its own token. A call that throws, or outlasts its bound, is shared with its waiters and never cached. Owned by
/// ISK-0007.
/// </summary>
internal sealed class ProbeGate<T>
{
    private readonly Func<CancellationToken, ValueTask<T>> _probe;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _bound;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private Task<T>? _call;
    private T? _last;
    private long _lastAt;
    private bool _hasLast;

    public ProbeGate(Func<CancellationToken, ValueTask<T>> probe, TimeSpan cacheDuration, TimeSpan bound, TimeProvider time)
    {
        _probe = probe;
        _cacheDuration = cacheDuration;
        _bound = bound;
        _time = time;
    }

    /// <summary>The cached result, the call in flight, or a new call, awaited with <paramref name="cancellationToken"/>.</summary>
    public async ValueTask<T> GetAsync(CancellationToken cancellationToken)
    {
        Task<T> call;
        lock (_gate)
        {
            if (_hasLast && _cacheDuration > TimeSpan.Zero && _time.GetElapsedTime(_lastAt) <= _cacheDuration)
            {
                return _last!;
            }

            if (_call is null || _call.IsCompleted)
            {
                _call = CallAsync();
            }

            call = _call;
        }

        return await call.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> CallAsync()
    {
        // Yield first, so the call never completes inside the lock that started it.
        await Task.Yield();
        using var bound = new CancellationTokenSource(_bound, _time);
        T value;
        try
        {
            // The wait ends at the bound even when the probe ignores its token.
            value = await _probe(bound.Token).AsTask().WaitAsync(bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException();
        }

        lock (_gate)
        {
            _last = value;
            _lastAt = _time.GetTimestamp();
            _hasLast = true;
        }

        return value;
    }
}

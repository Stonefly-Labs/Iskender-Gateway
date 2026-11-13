namespace Iskender.Infrastructure.Tests;

/// <summary>
/// A clock that moves only when told to. Timers, including those behind <c>Task.Delay</c> with this provider, fire when
/// <see cref="Advance"/> passes their due time, so a test controls expiry and bounded waits without sleeping.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override DateTimeOffset GetUtcNow() => _start.AddTicks(GetTimestamp());

    /// <summary>Moves the clock forward and fires every timer that has come due, in due order.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        long target;
        lock (_gate)
        {
            target = _now + by.Ticks;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = Math.Max(_now, next.Due);
                next.Rearm();
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private long _period = -1;

        public long Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
                _period = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = clock._now + Math.Max(0, dueTime.Ticks);
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        // Called under the clock's lock: a periodic timer is due again one period later, a one-shot timer is removed.
        public void Rearm()
        {
            clock._timers.Remove(this);
            if (_period > 0)
            {
                Due += _period;
                clock._timers.Add(this);
            }
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

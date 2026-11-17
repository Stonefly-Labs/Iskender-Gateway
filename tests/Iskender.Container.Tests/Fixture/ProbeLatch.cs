namespace Iskender.Container.Tests.Fixture;

/// <summary>Counts a stub's calls, lets a test wait for a number of them, and can hold calls until released.</summary>
internal sealed class ProbeLatch
{
    private readonly Lock _gate = new();
    private readonly List<(int Calls, TaskCompletionSource Signal)> _waiters = [];
    private TaskCompletionSource? _held;
    private int _calls;

    public int Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls;
            }
        }
    }

    public void Hold()
    {
        lock (_gate)
        {
            _held ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        TaskCompletionSource? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
        }

        held?.TrySetResult();
    }

    /// <summary>Completes once at least <paramref name="calls"/> calls have started.</summary>
    public Task WhenCalledAsync(int calls)
    {
        lock (_gate)
        {
            if (_calls >= calls)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((calls, signal));
            return signal.Task;
        }
    }

    public Task EnterAsync(CancellationToken cancellationToken)
    {
        Task held;
        List<TaskCompletionSource> ready;
        lock (_gate)
        {
            _calls++;
            held = _held?.Task ?? Task.CompletedTask;
            ready = [.. _waiters.Where(waiter => waiter.Calls <= _calls).Select(waiter => waiter.Signal)];
            _waiters.RemoveAll(waiter => waiter.Calls <= _calls);
        }

        foreach (var signal in ready)
        {
            signal.TrySetResult();
        }

        return held.WaitAsync(cancellationToken);
    }
}

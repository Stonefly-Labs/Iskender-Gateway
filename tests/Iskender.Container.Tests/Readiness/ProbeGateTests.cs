using Iskender.Infrastructure.Container.Health;
using Iskender.Infrastructure.Tests;

namespace Iskender.Container.Tests.Readiness;

/// <summary>Container contract, Readiness rule 8, and research R11: one call in flight, shared, and a cached result.</summary>
public class ProbeGateTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(7);

    private readonly ManualTimeProvider _clock = new();

    private readonly List<(TaskCompletionSource<int> Result, CancellationToken Token)> _calls = [];

    private readonly Lock _gate = new();

    private bool _hold;

    private int _next;

    private int Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.Count;
            }
        }
    }

    private ProbeGate<int> Gate(TimeSpan cache) => new(Probe, cache, Bound, _clock);

    // Each call returns the next number at once, or, while held, waits until the test completes it.
    private ValueTask<int> Probe(CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _calls.Add((result, cancellationToken));
            if (!_hold)
            {
                result.SetResult(++_next);
            }
        }

        return new(result.Task);
    }

    private async Task WhenCalledAsync(int count)
    {
        while (Calls < count)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task CallersThatArriveWhileACallRunsShareIt()
    {
        var gate = Gate(TimeSpan.FromSeconds(2));
        _hold = true;

        var first = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await WhenCalledAsync(1);
        var others = Enumerable.Range(0, 99).Select(_ => gate.GetAsync(TestContext.Current.CancellationToken).AsTask()).ToList();
        _calls[0].Result.SetResult(42);

        Assert.Equal(42, await first);
        Assert.All(await Task.WhenAll(others), value => Assert.Equal(42, value));
        Assert.Equal(1, Calls);
    }

    [Fact]
    public async Task AResultIsReusedInsideTheWindowAndReplacedAfterIt()
    {
        var gate = Gate(TimeSpan.FromSeconds(2));

        Assert.Equal(1, await gate.GetAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await gate.GetAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, await gate.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, Calls);
    }

    [Fact]
    public async Task WithNoCacheEachCallerThatFindsNoCallInFlightStartsOne()
    {
        var gate = Gate(TimeSpan.Zero);

        Assert.Equal(1, await gate.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await gate.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, Calls);
    }

    [Fact]
    public async Task WithNoCacheCallersStillShareACallInFlight()
    {
        var gate = Gate(TimeSpan.Zero);
        _hold = true;

        var first = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await WhenCalledAsync(1);
        var second = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        _calls[0].Result.SetResult(5);

        var values = await Task.WhenAll(first, second);
        Assert.Equal([5, 5], values);
        Assert.Equal(1, Calls);
    }

    [Fact]
    public async Task ACallerThatHangsUpDoesNotCancelTheSharedCall()
    {
        var gate = Gate(TimeSpan.FromSeconds(2));
        _hold = true;
        using var hangsUp = new CancellationTokenSource();

        var leaving = gate.GetAsync(hangsUp.Token).AsTask();
        await WhenCalledAsync(1);
        var staying = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await hangsUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        Assert.False(_calls[0].Token.IsCancellationRequested);
        _calls[0].Result.SetResult(9);
        Assert.Equal(9, await staying);
    }

    [Fact]
    public async Task AProbeThatThrowsFailsItsWaitersAndIsNotCached()
    {
        var gate = Gate(TimeSpan.FromSeconds(2));
        _hold = true;

        var first = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await WhenCalledAsync(1);
        var second = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        _calls[0].Result.SetException(new InvalidOperationException());

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        _hold = false;
        Assert.Equal(1, await gate.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, Calls);
    }

    [Fact]
    public async Task ACallThatOutlastsItsBoundEndsAndItsTokenIsCancelled()
    {
        var gate = Gate(TimeSpan.FromSeconds(2));
        _hold = true;

        var waiting = gate.GetAsync(TestContext.Current.CancellationToken).AsTask();
        await WhenCalledAsync(1);
        _clock.Advance(Bound);

        await Assert.ThrowsAsync<TimeoutException>(() => waiting);
        Assert.True(_calls[0].Token.IsCancellationRequested);
    }
}

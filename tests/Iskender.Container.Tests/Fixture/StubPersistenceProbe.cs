using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Fixture;

/// <summary>A persistence probe that returns a set result, counts its calls, and can hold each call until released.</summary>
internal sealed class StubPersistenceProbe(PersistenceHealthResult result) : IPersistenceHealthProbe
{
    private readonly ProbeLatch _latch = new();

    public StubPersistenceProbe()
        : this(PersistenceHealthResult.Healthy)
    {
    }

    public PersistenceHealthResult Result { get; set; } = result;

    public int Calls => _latch.Calls;

    /// <summary>Makes every later call wait until <see cref="Release"/>.</summary>
    public void Hold() => _latch.Hold();

    public void Release() => _latch.Release();

    public Task WhenCalledAsync(int calls) => _latch.WhenCalledAsync(calls);

    public async ValueTask<PersistenceHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        await _latch.EnterAsync(cancellationToken);
        return Result;
    }
}

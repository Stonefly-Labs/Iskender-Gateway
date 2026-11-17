using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Fixture;

/// <summary>A single-instance status that returns a set state and counts its reads. Reading it never blocks.</summary>
internal sealed class StubSingleInstanceStatus(SingleInstanceState state) : ISingleInstanceStatus
{
    private int _reads;

    public SingleInstanceState Value { get; set; } = state;

    public int Reads => Volatile.Read(ref _reads);

    public SingleInstanceState State
    {
        get
        {
            Interlocked.Increment(ref _reads);
            return Value;
        }
    }
}

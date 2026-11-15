namespace Iskender.Domain.Registry.Composition;

/// <summary>Where a container's composition lifecycle is (hosting contract, Lifetimes). Owned by ISK-0007.</summary>
public enum CompositionPhase
{
    /// <summary>The composition start-ups have not all finished.</summary>
    Starting,

    /// <summary>Every composition start-up has finished.</summary>
    Started,

    /// <summary>The composition start-ups have been stopped.</summary>
    Stopped,
}

/// <summary>The container's <see cref="CompositionPhase"/>, changed only by <see cref="CompositionLifecycle"/>. Owned by ISK-0007.</summary>
public sealed class CompositionStatus
{
    private int _phase;

    public CompositionPhase Phase
    {
        get => (CompositionPhase)Volatile.Read(ref _phase);
        internal set => Volatile.Write(ref _phase, (int)value);
    }
}

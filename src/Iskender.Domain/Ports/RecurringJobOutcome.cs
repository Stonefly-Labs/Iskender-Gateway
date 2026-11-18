namespace Iskender.Domain.Ports;

/// <summary>How a recurring job's run ended. Owned by ISK-0009.</summary>
public enum RecurringJobOutcome
{
    /// <summary>The run finished its work. The next run starts from the beginning.</summary>
    Completed,

    /// <summary>The run stopped with work left. The cursor is kept for the next run.</summary>
    Partial,
}

using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The state of a work item (work contract, Work items rule 5). Owned by ISK-0009.</summary>
public enum WorkItemState
{
    /// <summary>Code <c>pending</c>: due for dispatch at its due time.</summary>
    Pending,

    /// <summary>Code <c>completed</c>: final, its apply committed.</summary>
    Completed,

    /// <summary>Code <c>poisoned</c>: final, quarantined after a permanent failure or its maximum attempts.</summary>
    Poisoned,
}

/// <summary>The stored codes of <see cref="WorkItemState"/>. Owned by ISK-0009.</summary>
public static class WorkItemStates
{
    public static string ToCode(this WorkItemState state) => state switch
    {
        WorkItemState.Pending => "pending",
        WorkItemState.Completed => "completed",
        WorkItemState.Poisoned => "poisoned",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>Reads a stored code. Anything else returns <c>false</c>.</summary>
    public static bool TryParse(string? code, out WorkItemState state)
    {
        foreach (var candidate in Enum.GetValues<WorkItemState>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal))
            {
                state = candidate;
                return true;
            }
        }

        state = default;
        return false;
    }
}

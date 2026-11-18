using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The class of a work item attempt's failure (work contract, Retry and poison rule 1). Owned by ISK-0009.</summary>
public enum WorkFailureClass
{
    /// <summary>Code <c>transient</c>: retried with backoff.</summary>
    Transient,

    /// <summary>Code <c>permanent</c>: poisoned at once.</summary>
    Permanent,

    /// <summary>Code <c>unclassified</c>: an unexpected failure, retried as transient.</summary>
    Unclassified,

    /// <summary>Code <c>abandoned</c>: the attempt ended with no recorded result, such as a crash.</summary>
    Abandoned,
}

/// <summary>The stored and reported codes of <see cref="WorkFailureClass"/>. Owned by ISK-0009.</summary>
public static class WorkFailureClasses
{
    public static string ToCode(this WorkFailureClass failureClass) => failureClass switch
    {
        WorkFailureClass.Transient => "transient",
        WorkFailureClass.Permanent => "permanent",
        WorkFailureClass.Unclassified => "unclassified",
        WorkFailureClass.Abandoned => "abandoned",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>Reads a stored code. Anything else returns <c>false</c>.</summary>
    public static bool TryParse(string? code, out WorkFailureClass failureClass)
    {
        foreach (var candidate in Enum.GetValues<WorkFailureClass>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal))
            {
                failureClass = candidate;
                return true;
            }
        }

        failureClass = default;
        return false;
    }
}

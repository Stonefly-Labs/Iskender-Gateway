using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>A classified attempt failure: its class and the code recorded for it. Owned by ISK-0009.</summary>
internal readonly record struct WorkFailure(WorkFailureClass Class, ErrorCode Code);

/// <summary>
/// Classifies a handler's failure exactly as <c>work</c> contract Retry and poison rule 1. Pure. Owned by ISK-0009.
/// </summary>
internal static class WorkClassifier
{
    /// <summary>The handler did not finish within its timeout.</summary>
    public static WorkFailure Timeout => new(WorkFailureClass.Transient, ErrorCodes.WorkHandlerTimeout);

    /// <summary>The handler was cancelled because the worker is shutting down.</summary>
    public static WorkFailure Shutdown => new(WorkFailureClass.Transient, ErrorCodes.WorkHandlerCancelled);

    /// <summary>The steps before the handler did not finish within their bound, so the handler never ran.</summary>
    public static WorkFailure PersistenceTimeout => new(WorkFailureClass.Transient, ErrorCodes.PersistenceTimeout);

    /// <summary>An attempt that crashed, was killed, or lost its shard's lease after its attempt record.</summary>
    public static WorkFailure Abandoned => new(WorkFailureClass.Abandoned, ErrorCodes.WorkAttemptAbandoned);

    /// <summary>
    /// <c>WorkFailures.Permanent(code)</c> is <c>permanent</c> with its code; <c>persistence.unavailable</c>,
    /// <c>persistence.timeout</c>, <c>persistence.concurrency_conflict</c>, and every <c>coordination.*</c> code except
    /// <c>coordination.capability_unsupported</c> are <c>transient</c> with their code; anything else is
    /// <c>unclassified</c> with <c>core.internal</c>. Timeout and shutdown are told apart by the caller, which knows which
    /// token fired, and use <see cref="Timeout"/> and <see cref="Shutdown"/>.
    /// </summary>
    public static WorkFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is not GatewayException failure)
        {
            return Unclassified;
        }

        if (WorkFailures.IsPermanent(failure))
        {
            return new(WorkFailureClass.Permanent, failure.Code);
        }

        return IsTransient(failure.Code) ? new(WorkFailureClass.Transient, failure.Code) : Unclassified;
    }

    private static WorkFailure Unclassified => new(WorkFailureClass.Unclassified, ErrorCodes.Internal);

    private static bool IsTransient(ErrorCode code) =>
        ReferenceEquals(code, ErrorCodes.PersistenceUnavailable)
        || ReferenceEquals(code, ErrorCodes.PersistenceTimeout)
        || ReferenceEquals(code, ErrorCodes.PersistenceConcurrencyConflict)
        || (code.Id.StartsWith("coordination.", StringComparison.Ordinal) && !ReferenceEquals(code, ErrorCodes.CoordinationCapabilityUnsupported));
}

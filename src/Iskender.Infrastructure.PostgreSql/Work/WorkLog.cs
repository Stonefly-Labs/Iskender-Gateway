using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The <c>work</c> contract's events 8001 to 8012 under category <c>Iskender.Work</c>, with fixed IDs, names, levels, and
/// templates (FR-033). Fields are only module, kind, job, outcome, failure class, error code, and attempt: never a scope
/// ID, item ID, idempotency key, payload, or configuration value. Owned by ISK-0009.
/// </summary>
internal static class WorkLog
{
    /// <summary>The logger category.</summary>
    public const string Category = "Iskender.Work";

    private static readonly Action<ILogger, Exception?> DispatcherStartedEvent =
        LoggerMessage.Define(
            LogLevel.Information, new EventId(8001, "WorkDispatcherStarted"),
            "Iskender work dispatcher started.");

    private static readonly Action<ILogger, Exception?> DispatcherStoppingEvent =
        LoggerMessage.Define(
            LogLevel.Information, new EventId(8002, "WorkDispatcherStopping"),
            "Iskender work dispatcher stopping.");

    private static readonly Action<ILogger, string, Exception?> DispatcherStoppedEvent =
        LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(8003, "WorkDispatcherStopped"),
            "Iskender work dispatcher stopped: outcome {Outcome}.");

    private static readonly Action<ILogger, string, string, Exception?> ItemEnqueuedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug, new EventId(8004, "WorkItemEnqueued"),
            "Iskender work item enqueued: kind {Kind}, outcome {Outcome}.");

    private static readonly Action<ILogger, string, int, Exception?> ItemCompletedEvent =
        LoggerMessage.Define<string, int>(
            LogLevel.Debug, new EventId(8005, "WorkItemCompleted"),
            "Iskender work item completed: kind {Kind}, attempt {Attempt}.");

    private static readonly Action<ILogger, string, int, string, string, Exception?> RetryScheduledEvent =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Information, new EventId(8006, "WorkItemRetryScheduled"),
            "Iskender work item retry scheduled: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}.");

    private static readonly Action<ILogger, string, int, string, string, Exception?> ItemPoisonedEvent =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Warning, new EventId(8007, "WorkItemPoisoned"),
            "Iskender work item poisoned: kind {Kind}, attempt {Attempt}, class {FailureClass}, code {Code}.");

    private static readonly Action<ILogger, string, string, Exception?> ItemsSkewedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(8008, "WorkItemsSkewed"),
            "Iskender work items skipped for version skew: kind {Kind}, outcome {Outcome}.");

    private static readonly Action<ILogger, string, Exception?> DirectoryRowMalformedEvent =
        LoggerMessage.Define<string>(
            LogLevel.Warning, new EventId(8009, "WorkDirectoryRowMalformed"),
            "Iskender skipped a malformed scope directory row: module {Module}.");

    private static readonly Action<ILogger, string, int, string, Exception?> ApplyDiscardedEvent =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Information, new EventId(8010, "WorkApplyDiscarded"),
            "Iskender work item apply discarded: kind {Kind}, attempt {Attempt}, outcome {Outcome}.");

    private static readonly Action<ILogger, string, string, Exception?> JobRunEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, new EventId(8011, "WorkJobRun"),
            "Iskender work job run: job {Job}, outcome {Outcome}.");

    private static readonly Action<ILogger, string, string, Exception?> PassFailedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(8012, "WorkPassFailed"),
            "Iskender work pass failed: class {FailureClass}, code {Code}.");

    public static void DispatcherStarted(ILogger logger) => DispatcherStartedEvent(logger, null);

    public static void DispatcherStopping(ILogger logger) => DispatcherStoppingEvent(logger, null);

    /// <summary>Event 8003, with outcome <c>drained</c> or <c>cancelled</c>.</summary>
    public static void DispatcherStopped(ILogger logger, bool drained) =>
        DispatcherStoppedEvent(logger, drained ? WorkOutcomes.Drained : WorkOutcomes.Cancelled, null);

    /// <summary>Event 8004, with outcome <c>created</c> or <c>existing</c>.</summary>
    public static void ItemEnqueued(ILogger logger, WorkKind kind, bool created) =>
        ItemEnqueuedEvent(logger, kind.ToString(), created ? WorkOutcomes.Created : WorkOutcomes.Existing, null);

    public static void ItemCompleted(ILogger logger, WorkKind kind, int attempt) => ItemCompletedEvent(logger, kind.ToString(), attempt, null);

    public static void RetryScheduled(ILogger logger, WorkKind kind, int attempt, WorkFailure failure) =>
        RetryScheduledEvent(logger, kind.ToString(), attempt, failure.Class.ToCode(), failure.Code.Id, null);

    public static void ItemPoisoned(ILogger logger, WorkKind kind, int attempt, WorkFailure failure) =>
        ItemPoisonedEvent(logger, kind.ToString(), attempt, failure.Class.ToCode(), failure.Code.Id, null);

    /// <summary>Event 8008, with outcome <c>appeared</c> or <c>cleared</c>.</summary>
    public static void ItemsSkewed(ILogger logger, WorkKind kind, bool appeared) =>
        ItemsSkewedEvent(logger, kind.ToString(), appeared ? WorkOutcomes.Appeared : WorkOutcomes.Cleared, null);

    public static void DirectoryRowMalformed(ILogger logger, string module) => DirectoryRowMalformedEvent(logger, module, null);

    /// <summary>Event 8010, with outcome <c>fencing_rejected</c>, <c>superseded</c>, or <c>lease_lost</c>.</summary>
    public static void ApplyDiscarded(ILogger logger, WorkKind kind, int attempt, string outcome) =>
        ApplyDiscardedEvent(logger, kind.ToString(), attempt, WorkOutcomes.RequireDiscard(outcome), null);

    /// <summary>Event 8011, with outcome <c>completed</c>, <c>partial</c>, <c>held</c>, or <c>failed</c>.</summary>
    public static void JobRun(ILogger logger, string job, string outcome) => JobRunEvent(logger, job, WorkOutcomes.RequireJob(outcome), null);

    public static void PassFailed(ILogger logger, WorkFailureClass failureClass, ErrorCode code) =>
        PassFailedEvent(logger, failureClass.ToCode(), code.Id, null);
}

/// <summary>The fixed outcome values of the work events, activities, and metrics. Owned by ISK-0009.</summary>
internal static class WorkOutcomes
{
    public const string Drained = "drained";
    public const string Cancelled = "cancelled";
    public const string Created = "created";
    public const string Existing = "existing";
    public const string Appeared = "appeared";
    public const string Cleared = "cleared";
    public const string FencingRejected = "fencing_rejected";
    public const string Superseded = "superseded";
    public const string LeaseLost = "lease_lost";
    public const string Completed = "completed";
    public const string Partial = "partial";
    public const string Held = "held";
    public const string Failed = "failed";

    public static string RequireDiscard(string outcome) =>
        outcome is FencingRejected or Superseded or LeaseLost ? outcome : throw new GatewayException(ErrorCodes.ValueInvalid);

    public static string RequireJob(string outcome) =>
        outcome is Completed or Partial or Held or Failed ? outcome : throw new GatewayException(ErrorCodes.ValueInvalid);
}

using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Diagnostics;

/// <summary>
/// The contract's persistence events, with fixed IDs, names, levels, and templates (FR-032, research R18). Event 4005
/// belongs to the retry helper in the ports, which cannot reach this assembly, so it is defined there. Fields are
/// only module, migration version, operation kind, outcome, failure class, reason code, and durations: never an owner
/// ID, SQL, a parameter value, or a connection detail (FR-026). Owned by ISK-0004.
/// </summary>
internal static class PersistenceLog
{
    private static readonly Action<ILogger, string, string, string, Exception?> ConnectionFailed =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning, new EventId(4001, "PersistenceConnectionFailed"),
            "Persistence connection failed: module {Module}, operation {Operation}, class {FailureClass}.");

    private static readonly Action<ILogger, string, string, string, Exception?> RolledBack =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug, new EventId(4002, "UnitOfWorkRolledBack"),
            "Unit of work rolled back: module {Module}, operation {Operation}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, string, Exception?> Faulted =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning, new EventId(4003, "UnitOfWorkFaulted"),
            "Unit of work faulted: module {Module}, operation {Operation}, class {FailureClass}.");

    private static readonly Action<ILogger, string, string, Exception?> CommitOutcomeUnknown =
        LoggerMessage.Define<string, string>(
            LogLevel.Error, new EventId(4004, "UnitOfWorkCommitOutcomeUnknown"),
            "Unit of work commit outcome unknown: module {Module}, operation {Operation}. The connection broke during commit.");

    private static readonly Action<ILogger, string, string, double, Exception?> LockTimedOut =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Warning, new EventId(4006, "AdvisoryLockTimedOut"),
            "Advisory lock wait expired: module {Module}, operation {Operation}, waited {DurationMs} ms.");

    private static readonly Action<ILogger, string, Exception?> MigrationStarted =
        LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(4007, "MigrationStarted"),
            "Migration started: operation {Operation}.");

    private static readonly Action<ILogger, string, double, Exception?> MigrationLockWaiting =
        LoggerMessage.Define<string, double>(
            LogLevel.Information, new EventId(4008, "MigrationLockWaiting"),
            "Migration lock held by another migrator: operation {Operation}, waited {DurationMs} ms so far.");

    private static readonly Action<ILogger, string, int, string, double, Exception?> MigrationStepApplied =
        LoggerMessage.Define<string, int, string, double>(
            LogLevel.Information, new EventId(4009, "MigrationStepApplied"),
            "Migration step done: module {Module}, version {Version}, operation {Operation}, {DurationMs} ms.");

    private static readonly Action<ILogger, string, int, string, string, Exception?> MigrationStepFailed =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Error, new EventId(4010, "MigrationStepFailed"),
            "Migration step failed: module {Module}, version {Version}, class {FailureClass}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, double, Exception?> MigrationCompleted =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Information, new EventId(4011, "MigrationCompleted"),
            "Migration finished: operation {Operation}, outcome {Outcome}, {DurationMs} ms.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> MigrationRefused =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Error, new EventId(4012, "MigrationRefused"),
            "Migration refused: code {Outcome}, reason {Reason}, module {Module}, version {Version}.");

    private static readonly Action<ILogger, string, string, string, Exception?> HealthChanged =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information, new EventId(4013, "PersistenceHealthChanged"),
            "Persistence health changed: status {Outcome}, reason {Reason}, previous reason {PreviousReason}.");

    private static readonly Action<ILogger, string, string, string, Exception?> UnexpectedFailure =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error, new EventId(4014, "PersistenceUnexpectedFailure"),
            "Unexpected persistence failure: module {Module}, operation {Operation}, SQLSTATE class {SqlStateClass}.");

    /// <summary>The text written for a field that has no value, such as the version of a set-level refusal.</summary>
    public const string None = "(none)";

    public static void LogConnectionFailed(ILogger logger, string module, string operation, PersistenceFailureClass failureClass) =>
        ConnectionFailed(logger, module, operation, Text(failureClass), null);

    public static void LogRolledBack(ILogger logger, string module, string operation, string reason) =>
        RolledBack(logger, module, operation, reason, null);

    public static void LogFaulted(ILogger logger, string module, string operation, PersistenceFailureClass failureClass) =>
        Faulted(logger, module, operation, Text(failureClass), null);

    public static void LogCommitOutcomeUnknown(ILogger logger, string module, string operation) =>
        CommitOutcomeUnknown(logger, module, operation, null);

    public static void LogLockTimedOut(ILogger logger, string module, string operation, TimeSpan waited) =>
        LockTimedOut(logger, module, operation, waited.TotalMilliseconds, null);

    public static void LogMigrationStarted(ILogger logger, string operation) => MigrationStarted(logger, operation, null);

    public static void LogMigrationLockWaiting(ILogger logger, string operation, TimeSpan waited) =>
        MigrationLockWaiting(logger, operation, waited.TotalMilliseconds, null);

    public static void LogMigrationStepApplied(ILogger logger, MigrationIdentity migration, string operation, TimeSpan duration) =>
        MigrationStepApplied(logger, migration.Module, migration.Version, operation, duration.TotalMilliseconds, null);

    public static void LogMigrationStepFailed(ILogger logger, MigrationIdentity migration, PersistenceFailureClass failureClass, string reason) =>
        MigrationStepFailed(logger, migration.Module, migration.Version, Text(failureClass), reason, null);

    public static void LogMigrationCompleted(ILogger logger, string operation, string outcome, TimeSpan duration) =>
        MigrationCompleted(logger, operation, outcome, duration.TotalMilliseconds, null);

    public static void LogMigrationRefused(ILogger logger, string code, string reason, string? module, int? version) =>
        MigrationRefused(logger, code, reason, module ?? None, version?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? None, null);

    public static void LogHealthChanged(ILogger logger, PersistenceHealthResult result, PersistenceHealthReason? previous) =>
        HealthChanged(logger, result.Status == PersistenceHealthStatus.Healthy ? "healthy" : "unhealthy", result.Reason.ToCode(), previous?.ToCode() ?? None, null);

    public static void LogUnexpectedFailure(ILogger logger, string module, string operation, string? sqlStateClass) =>
        UnexpectedFailure(logger, module, operation, sqlStateClass ?? None, null);

    /// <summary>The snake-case text of a failure class, used in every field and label.</summary>
    public static string Text(PersistenceFailureClass failureClass) => failureClass switch
    {
        PersistenceFailureClass.TransientConflict => "transient_conflict",
        PersistenceFailureClass.ConcurrencyConflict => "concurrency_conflict",
        PersistenceFailureClass.UniqueConflict => "unique_conflict",
        PersistenceFailureClass.Unavailable => "unavailable",
        PersistenceFailureClass.Timeout => "timeout",
        PersistenceFailureClass.Cancelled => "cancelled",
        PersistenceFailureClass.AuthenticationFailed => "authentication_failed",
        PersistenceFailureClass.ScopeViolation => "scope_violation",
        _ => "unexpected",
    };
}

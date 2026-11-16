using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Container.Diagnostics;

/// <summary>
/// The container events 7001 to 7006, with fixed IDs, names, levels, and templates (container contract, Diagnostics).
/// Fields are plane names, the release, error and reason codes, module and feature names, check names, exit codes,
/// exception type names, operation names, and counts: never a configuration value, secret reference name, connection
/// detail, file content, or request route value. Owned by ISK-0007.
/// </summary>
public static class ContainerLog
{
    /// <summary>The logger category every container event is written under.</summary>
    public const string Category = "Iskender.Container";

    /// <summary>The text written for an absent value.</summary>
    public const string Absent = "-";

    private const string StoppedTemplate = "Iskender container stopped: plane {Plane}, exit code {ExitCode}, failure {Failure}.";

    private static readonly Action<ILogger, string, string, Exception?> Starting =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, new EventId(7001, "ContainerStarting"),
            "Iskender container starting: plane {Plane}, release {Release}.");

    private static readonly Action<ILogger, string, int, string, string, string, Exception?> StartRefused =
        LoggerMessage.Define<string, int, string, string, string>(
            LogLevel.Error, new EventId(7002, "ContainerStartRefused"),
            "Iskender container start refused: code {Code}, exit code {ExitCode}, feature {Feature}, module {Module}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, string, Exception?> ReadinessChanged =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information, new EventId(7003, "ContainerReadinessChanged"),
            "Iskender readiness changed: check {Check}, from {From} to {To}.");

    private static readonly Action<ILogger, string, Exception?> StopRequested =
        LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(7004, "ContainerStopRequested"),
            "Iskender container stop requested: plane {Plane}.");

    private static readonly Action<ILogger, string, int, string, Exception?> Stopped =
        LoggerMessage.Define<string, int, string>(LogLevel.Information, new EventId(7005, "ContainerStopped"), StoppedTemplate);

    private static readonly Action<ILogger, string, int, string, Exception?> StoppedWithFailure =
        LoggerMessage.Define<string, int, string>(LogLevel.Error, new EventId(7005, "ContainerStopped"), StoppedTemplate);

    private static readonly Action<ILogger, string, string, int, Exception?> MigrationFinished =
        LoggerMessage.Define<string, string, int>(
            LogLevel.Information, new EventId(7006, "ContainerMigrationFinished"),
            "Iskender migration command finished: operation {Operation}, outcome {Outcome}, count {Count}.");

    /// <summary>Event 7001. <paramref name="plane"/> is <c>data_plane</c> or <c>control_plane</c>.</summary>
    public static void LogStarting(ILogger logger, string plane, string? release) => Starting(logger, plane, release ?? Absent, null);

    /// <summary>Event 7002, for a serving start that fails with exit code 2, 3, or 4. The feature, module, and reason are the refused feature's, or absent.</summary>
    public static void LogStartRefused(ILogger logger, string code, int exitCode, string? feature = null, string? module = null, string? reason = null) =>
        StartRefused(logger, code, exitCode, feature ?? Absent, module ?? Absent, reason ?? Absent, null);

    /// <summary>Event 7003. <paramref name="from"/> is <c>null</c> on the first evaluation.</summary>
    public static void LogReadinessChanged(ILogger logger, string check, string? from, string to) => ReadinessChanged(logger, check, from ?? Absent, to, null);

    /// <summary>Event 7004.</summary>
    public static void LogStopRequested(ILogger logger, string plane) => StopRequested(logger, plane, null);

    /// <summary>Event 7005, at Error level for a non-zero exit code. <paramref name="failure"/> is an exception's type name, never its message.</summary>
    public static void LogStopped(ILogger logger, string plane, int exitCode, string? failure = null) =>
        (exitCode == 0 ? Stopped : StoppedWithFailure)(logger, plane, exitCode, failure ?? Absent, null);

    /// <summary>Event 7006.</summary>
    public static void LogMigrationFinished(ILogger logger, string operation, string outcome, int count) => MigrationFinished(logger, operation, outcome, count, null);
}

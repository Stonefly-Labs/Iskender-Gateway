using Microsoft.Extensions.Logging;

namespace Iskender.Management.Migrations;

/// <summary>
/// Event 7006, written once for every <c>migrate</c> run whose arguments parse (container contract, Migration command rule
/// 5, and Diagnostics rules 6 and 7). It is a container event, under category <c>Iskender.Container</c>, defined here
/// because the migration command's outcome handling lives in this library (FR-025), which reaches no infrastructure
/// assembly (rule A3). Owned by ISK-0007.
/// </summary>
public static class MigrationLog
{
    /// <summary>The logger category the event is written under, the container events' category.</summary>
    public const string Category = "Iskender.Container";

    /// <summary>The text written for an absent code.</summary>
    public const string Absent = "-";

    private static readonly Action<ILogger, string, string, int, string, Exception?> MigrationFinished =
        LoggerMessage.Define<string, string, int, string>(
            LogLevel.Information, new EventId(7006, "ContainerMigrationFinished"),
            "Iskender migration command finished: operation {Operation}, outcome {Outcome}, count {Count}, code {Code}.");

    /// <summary>Event 7006. <paramref name="code"/> is the failure's error code for exit code 2, 3, or 5, or absent.</summary>
    public static void LogFinished(ILogger logger, string operation, string outcome, int count, string? code = null) =>
        MigrationFinished(logger, operation, outcome, count, code ?? Absent, null);
}

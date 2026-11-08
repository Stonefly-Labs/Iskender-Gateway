using Iskender.Core.Contracts.Configuration;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Registry;

/// <summary>
/// The three configuration events, with the contract's fixed IDs, names, levels, and templates (FR-037, research R12).
/// Every field is built from the schema, the path, the code, or a source name, never from a configuration value.
/// </summary>
internal static class ConfigurationLog
{
    internal const string None = "(none)";

    private static readonly Action<ILogger, string, string, string, string, string, int, Exception?> Validated =
        LoggerMessage.Define<string, string, string, string, string, int>(
            LogLevel.Information,
            new EventId(3001, "ConfigurationValidated"),
            "Configuration validated: revision {Revision}, environment {Environment}, sections {Sections}, base {BaseSource}, overlay {OverlaySource}, environment variables {EnvironmentVariableCount}.");

    private static readonly Action<ILogger, string, string, string, int, int, string, Exception?> FieldError =
        LoggerMessage.Define<string, string, string, int, int, string>(
            LogLevel.Warning,
            new EventId(3002, "ConfigurationFieldError"),
            "Configuration error {Code} at {Path} ({Source}:{Line}:{Column}, {Layer}).");

    private static readonly Action<ILogger, int, int, string, Exception?> Rejected =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Error,
            new EventId(3003, "ConfigurationRejected"),
            "Configuration rejected: {ErrorCount} error(s), {ReportedCount} reported, environment {Environment}.");

    public static void LogValidated(ILogger logger, ConfigurationSnapshot snapshot) =>
        Validated(
            logger,
            snapshot.Revision.Value,
            snapshot.EnvironmentName ?? None,
            string.Join(',', snapshot.Namespaces.Select(version => version.ToString())),
            snapshot.Layers.BaseSource ?? None,
            snapshot.Layers.OverlaySource ?? None,
            snapshot.Layers.EnvironmentVariableCount,
            null);

    /// <summary>Writes one 3002 event per reported error, then 3003. <paramref name="environmentName"/> is a validated name, or null when none was given or it failed <c>config.environment.invalid</c>.</summary>
    public static void LogRejected(ILogger logger, ConfigurationValidationReport report, string? environmentName)
    {
        foreach (var error in report.Errors)
        {
            var location = error.Location;
            FieldError(logger, error.Code.Id, error.Path, location.Source, location.Line, location.Column, LayerText(location.Layer), null);
        }

        Rejected(logger, report.TotalErrorCount, report.Errors.Count, environmentName ?? None, null);
    }

    private static string LayerText(ConfigurationLayer layer) => layer switch
    {
        ConfigurationLayer.Defaults => "defaults",
        ConfigurationLayer.Base => "base",
        ConfigurationLayer.Overlay => "overlay",
        _ => "environment",
    };
}

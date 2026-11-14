using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Diagnostics;

/// <summary>
/// The hosting events 6001 to 6003, with fixed IDs, names, levels, and templates (hosting contract, Diagnostics). Fields
/// are only module names, section names and versions, the environment name, the revision, and counts: never
/// configuration content. Owned by ISK-0006.
/// </summary>
internal static class HostingLog
{
    /// <summary>The logger category every hosting event is written under.</summary>
    public const string Category = "Iskender.Hosting";

    /// <summary>The text written for an environment that has no name.</summary>
    public const string None = "(none)";

    private static readonly Action<ILogger, string, string, string, string, Exception?> Composed =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Information, new EventId(6001, "IskenderComposed"),
            "Iskender composed: modules {Modules}, sections {Sections}, environment {Environment}, revision {Revision}.");

    private static readonly Action<ILogger, int, Exception?> StartRefused =
        LoggerMessage.Define<int>(
            LogLevel.Error, new EventId(6002, "IskenderStartRefused"),
            "Iskender start refused: configuration rejected with {ErrorCount} error(s).");

    private static readonly Action<ILogger, string, string, Exception?> RegistrationMissing =
        LoggerMessage.Define<string, string>(
            LogLevel.Error, new EventId(6003, "IskenderRegistrationMissing"),
            "Iskender registration missing: {Missing} required by {RequiredBy}.");

    /// <summary>Event 6001, written once when the configuration is valid, naming the revision ISK-0003's 3001 names.</summary>
    public static void LogComposed(ILogger logger, IEnumerable<string> modules, IEnumerable<SectionSchema> sections, string? environment, ConfigurationRevision revision) =>
        Composed(logger, ModulesText(modules), SectionsText(sections), environment ?? None, revision.Value, null);

    /// <summary>Event 6002, written when the configuration was rejected, with the report's total error count.</summary>
    public static void LogStartRefused(ILogger logger, int errorCount) => StartRefused(logger, errorCount, null);

    /// <summary>
    /// Event 6003. <paramref name="missing"/> is a module name or <see cref="CoordinatorMissing"/>, and
    /// <paramref name="requiredBy"/> a module name, <c>configuration</c>, or <c>MapIskender</c>.
    /// </summary>
    public static void LogRegistrationMissing(ILogger logger, string missing, string requiredBy) => RegistrationMissing(logger, missing, requiredBy, null);

    /// <summary>The module names joined by <c>,</c>, in ordinal order.</summary>
    public static string ModulesText(IEnumerable<string> modules) => string.Join(',', modules.Order(StringComparer.Ordinal));

    /// <summary>The <c>name@version</c> pairs joined by <c>,</c>, in ordinal order.</summary>
    public static string SectionsText(IEnumerable<SectionSchema> sections) =>
        string.Join(',', sections.Select(section => $"{section.Namespace}@{section.Version}").Order(StringComparer.Ordinal));

    /// <summary>The <c>{Missing}</c> text for a coordinator that configuration selects and nothing registered: <c>&lt;kind&gt; coordinator for &lt;primitive&gt;</c>.</summary>
    public static string CoordinatorMissing(CoordinatorKind kind, CoordinationPrimitive primitive) => $"{kind.ToCode()} coordinator for {primitive.ToCode()}";
}

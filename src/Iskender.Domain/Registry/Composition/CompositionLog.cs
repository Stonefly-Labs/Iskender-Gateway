using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// The hosting events 6001 to 6004, with fixed IDs, names, levels, and templates (hosting contract, Diagnostics). Fields
/// are only module, feature, and section names, section versions, the environment name, the revision, reason codes, and
/// counts: never configuration content. Owned by ISK-0006, as Hosting's log; moved here by ISK-0007, which adds 6004.
/// </summary>
public static class CompositionLog
{
    /// <summary>
    /// The logger category every hosting event is written under, in both planes. A property rather than a constant, since
    /// a constant's value reads as a dependency on the <c>Iskender.Hosting</c> namespace, which <c>Iskender.Domain</c> must not have.
    /// </summary>
    public static string Category => "Iskender.Hosting";

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

    private static readonly Action<ILogger, string, string, Exception?> Missing =
        LoggerMessage.Define<string, string>(
            LogLevel.Error, new EventId(6003, "IskenderRegistrationMissing"),
            "Iskender registration missing: {Missing} required by {RequiredBy}.");

    private static readonly Action<ILogger, string, string, string, Exception?> MandatoryRefused =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error, new EventId(6004, "IskenderMandatoryFeatureRefused"),
            "Iskender start refused: mandatory feature {Feature} of module {Module} refused with reason {Reason}.");

    /// <summary>Event 6001, written once when the configuration is valid, naming the revision ISK-0003's 3001 names.</summary>
    public static void LogComposed(ILogger logger, IEnumerable<string> modules, IEnumerable<SectionSchema> sections, string? environment, ConfigurationRevision revision) =>
        Composed(logger, ModulesText(modules), SectionsText(sections), environment ?? None, revision.Value, null);

    /// <summary>Event 6002, written when the configuration was rejected, with the report's total error count.</summary>
    public static void LogStartRefused(ILogger logger, int errorCount) => StartRefused(logger, errorCount, null);

    /// <summary>
    /// Event 6003. <paramref name="missing"/> is a module name, <c>hosting</c> for a missing composition root, or
    /// <see cref="CoordinatorMissing"/>, and <paramref name="requiredBy"/> a module name, <c>configuration</c>, or <c>MapIskender</c>.
    /// </summary>
    public static void LogRegistrationMissing(ILogger logger, string missing, string requiredBy) => Missing(logger, missing, requiredBy, null);

    /// <summary>Event 6004, for a refused feature with <c>MandatoryPath</c>. Every field is a code-defined name.</summary>
    public static void LogMandatoryFeatureRefused(ILogger logger, string feature, string module, string reason) => MandatoryRefused(logger, feature, module, reason, null);

    /// <summary>Writes 6003, and returns the <c>hosting.registration_missing</c> failure to throw, naming <paramref name="missing"/> and <paramref name="requiredBy"/>.</summary>
    public static GatewayException RegistrationMissing(ILogger logger, string missing, string requiredBy)
    {
        LogRegistrationMissing(logger, missing, requiredBy);
        return CompositionModules.Missing(missing, requiredBy);
    }

    /// <summary>The module names joined by <c>,</c>, in ordinal order.</summary>
    public static string ModulesText(IEnumerable<string> modules) => string.Join(',', modules.Order(StringComparer.Ordinal));

    /// <summary>The <c>name@version</c> pairs joined by <c>,</c>, in ordinal order.</summary>
    public static string SectionsText(IEnumerable<SectionSchema> sections) =>
        string.Join(',', sections.Select(section => $"{section.Namespace}@{section.Version}").Order(StringComparer.Ordinal));

    /// <summary>The <c>{Missing}</c> text for a coordinator that configuration selects and nothing registered: <c>&lt;kind&gt; coordinator for &lt;primitive&gt;</c>.</summary>
    public static string CoordinatorMissing(CoordinatorKind kind, CoordinationPrimitive primitive) => $"{kind.ToCode()} coordinator for {primitive.ToCode()}";
}

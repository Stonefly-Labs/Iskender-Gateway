using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Registry;

/// <summary>
/// Validates a load request into a report. This is the only public route to a snapshot (FR-022, FR-023). It reads only the
/// supplied sources: it opens no file of its own, no connection, and no secret.
/// </summary>
public static class ConfigurationValidator
{
    private const string EnvironmentPath = "(environment)";

    public static ConfigurationValidationReport Validate(ConfigurationLoadRequest request, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        logger ??= NullLogger.Instance;

        var issues = new FieldIssues();

        // The environment name is checked before any document is read. An invalid one is reported alongside any document
        // errors, and like them it stops field validation (contract, Active environment).
        if (request.EnvironmentName is { } name && !IsEnvironmentName(name))
        {
            issues.Add(ErrorCodes.ConfigEnvironmentInvalid, EnvironmentPath, ConfigurationSourceLocation.None, ErrorTextTemplates.EnvironmentInvalid());
        }

        var baseRoot = Read(request.BaseDocument, ConfigurationLayer.Base, issues);
        var overlayRoot = Read(request.OverlayDocument, ConfigurationLayer.Overlay, issues);

        // A document error or an invalid environment name stops field validation, so the report holds only those (data model, Flow and states).
        if (issues.Count > 0)
        {
            return Fail(issues, request.EnvironmentName, logger);
        }

        // Each layer is checked for unknown names before merging, so an error names the layer that introduced the name (FR-017).
        var validator = new SchemaValidator(request.Registry, request.EnvironmentName, issues);
        var baseTree = validator.Iskender(baseRoot);
        var overlayTree = validator.Iskender(overlayRoot);
        foreach (var tree in new[] { baseTree, overlayTree })
        {
            if (tree is not null)
            {
                validator.CheckUnknown(tree);
            }
        }

        var (environmentTree, applied) = EnvironmentVariableReader.Read(request.Environment, request.Registry, issues);
        var sections = validator.Validate(OverlayMerger.Merge(baseTree, overlayTree, environmentTree));
        if (issues.Count > 0)
        {
            return Fail(issues, request.EnvironmentName, logger);
        }

        var layers = new ConfigurationLayers(request.BaseDocument?.Name, request.OverlayDocument?.Name, applied);
        var snapshot = ValidatedSnapshot.Create(sections, request.EnvironmentName, layers);
        ConfigurationLog.LogValidated(logger, snapshot);
        return ConfigurationValidationReport.Success(snapshot);
    }

    /// <summary>1 to 32 characters of a-z, 0-9, and -. A missing name is production-grade, not an error (TR-6).</summary>
    internal static bool IsEnvironmentName(string name) =>
        name.Length is >= 1 and <= 32 && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static ObjectNode? Read(ConfigurationDocumentSource? source, ConfigurationLayer layer, FieldIssues issues)
    {
        if (source is null)
        {
            return null;
        }

        var read = source.Format == ConfigurationDocumentFormat.Yaml
            ? YamlDocumentReader.Read(source, layer)
            : JsonDocumentReader.Read(source, layer);
        if (read.Error is { } error)
        {
            issues.Add(error.Code, DocumentError.Path, error.Location, ErrorTextTemplates.Document(error.Code));
        }

        return read.Root;
    }

    private static ConfigurationValidationReport Fail(FieldIssues issues, string? environmentName, ILogger logger)
    {
        var (reported, total) = issues.Truncated();
        var report = ConfigurationValidationReport.Failure(reported, total);
        ConfigurationLog.LogRejected(logger, report, environmentName);
        return report;
    }
}

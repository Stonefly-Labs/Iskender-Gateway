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

        // An invalid environment name is reported before any document is read, and stops field validation (research R13).
        if (request.EnvironmentName is { } name && !IsEnvironmentName(name))
        {
            issues.Add(ErrorCodes.ConfigEnvironmentInvalid, EnvironmentPath, ConfigurationSourceLocation.None, ErrorTextTemplates.EnvironmentInvalid());
            return Fail(issues);
        }

        var baseRoot = Read(request.BaseDocument, ConfigurationLayer.Base, issues);
        Read(request.OverlayDocument, ConfigurationLayer.Overlay, issues);

        // A document error stops field validation, so the report holds only each document's first fault (data model, Flow and states).
        if (issues.Count > 0)
        {
            return Fail(issues);
        }

        var validator = new SchemaValidator(request.Registry, request.EnvironmentName, issues);
        var iskender = validator.Iskender(baseRoot);
        if (iskender is not null)
        {
            validator.CheckUnknown(iskender);
        }

        var sections = validator.Validate(iskender);
        if (issues.Count > 0)
        {
            return Fail(issues);
        }

        var layers = new ConfigurationLayers(request.BaseDocument?.Name, request.OverlayDocument?.Name, 0);
        return ConfigurationValidationReport.Success(ValidatedSnapshot.Create(sections, request.EnvironmentName, layers));
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

    private static ConfigurationValidationReport Fail(FieldIssues issues)
    {
        var (reported, total) = issues.Truncated();
        return ConfigurationValidationReport.Failure(reported, total);
    }
}

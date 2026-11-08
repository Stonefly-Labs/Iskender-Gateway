using Iskender.Core.Contracts.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Registry;

/// <summary>
/// Validates a load request into a report. This is the only public route to a snapshot (FR-022, FR-023). It reads only the
/// supplied sources: it opens no file of its own, no connection, and no secret.
/// </summary>
public static class ConfigurationValidator
{
    public static ConfigurationValidationReport Validate(ConfigurationLoadRequest request, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        logger ??= NullLogger.Instance;

        var issues = new FieldIssues();
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

    private static ObjectNode? Read(ConfigurationDocumentSource? source, ConfigurationLayer layer, FieldIssues issues)
    {
        if (source is null)
        {
            return null;
        }

        var read = JsonDocumentReader.Read(source, layer);
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

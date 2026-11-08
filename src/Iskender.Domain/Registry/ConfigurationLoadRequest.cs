using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>What to validate: the registry, at most one base document, at most one overlay document, at most one set of environment variables, and the active environment name.</summary>
public sealed class ConfigurationLoadRequest
{
    public ConfigurationLoadRequest(
        ConfigurationSchemaRegistry registry,
        ConfigurationDocumentSource? baseDocument = null,
        ConfigurationDocumentSource? overlayDocument = null,
        ConfigurationEnvironmentSource? environment = null,
        string? environmentName = null)
    {
        Registry = registry ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        BaseDocument = baseDocument;
        OverlayDocument = overlayDocument;
        Environment = environment;
        EnvironmentName = environmentName;
    }

    public ConfigurationSchemaRegistry Registry { get; }

    public ConfigurationDocumentSource? BaseDocument { get; }

    public ConfigurationDocumentSource? OverlayDocument { get; }

    public ConfigurationEnvironmentSource? Environment { get; }

    /// <summary>The active environment, checked by the validator. Null means none, which is production-grade (TR-6).</summary>
    public string? EnvironmentName { get; }
}

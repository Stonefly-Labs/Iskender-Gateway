namespace Iskender.Domain.Registry;

/// <summary>The declared format of a configuration document.</summary>
public enum ConfigurationDocumentFormat
{
    /// <summary>RFC 8259 JSON: no comments, no trailing commas.</summary>
    Json,

    /// <summary>The JSON-equivalent YAML subset of the configuration contract.</summary>
    Yaml,
}

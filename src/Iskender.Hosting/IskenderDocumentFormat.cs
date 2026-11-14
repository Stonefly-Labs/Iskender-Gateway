using Iskender.Core.Contracts.Versioning;

namespace Iskender.Hosting;

/// <summary>The declared format of an Iskender configuration document.</summary>
[ContractVersion("hosting", 1)]
public enum IskenderDocumentFormat
{
    /// <summary>RFC 8259 JSON: no comments, no trailing commas.</summary>
    Json,

    /// <summary>The JSON-equivalent YAML subset of the configuration contract.</summary>
    Yaml,
}

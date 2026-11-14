using Iskender.Core.Contracts.Versioning;

namespace Iskender.Hosting;

/// <summary>
/// What <c>AddIskender(...)</c> reads its configuration from. The callback sets these once, and the values are copied, so a
/// later change to this object has no effect (hosting contract, Registration rule 2).
/// </summary>
[ContractVersion("hosting", 1)]
public sealed class IskenderOptions
{
    /// <summary>The base document, or <c>null</c> for none.</summary>
    public IskenderConfigurationDocument? BaseDocument { get; set; }

    /// <summary>The overlay document, applied over the base, or <c>null</c> for none.</summary>
    public IskenderConfigurationDocument? OverlayDocument { get; set; }

    /// <summary>Whether <c>ISKENDER__</c> process environment variables are applied. Off unless the host opts in.</summary>
    public bool ReadEnvironmentVariables { get; set; }

    /// <summary>The environment name passed to validation, or <c>null</c> for none.</summary>
    public string? EnvironmentName { get; set; }
}

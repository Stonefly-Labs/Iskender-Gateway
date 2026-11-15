using Iskender.Domain.Registry.Composition;

namespace Iskender.Management;

/// <summary>
/// What <c>AddIskenderControlPlane(...)</c> reads its configuration from. The callback sets these once, and the values are
/// copied, so a later change to this object has no effect (hosting contract, Registration rules 1 and 2). Owned by
/// ISK-0007.
/// </summary>
public sealed class ControlPlaneOptions
{
    /// <summary>The base document, or <c>null</c> for none.</summary>
    public CompositionDocument? BaseDocument { get; set; }

    /// <summary>The overlay document, applied over the base, or <c>null</c> for none.</summary>
    public CompositionDocument? OverlayDocument { get; set; }

    /// <summary>Whether <c>ISKENDER__</c> process environment variables are applied. Off unless the host opts in.</summary>
    public bool ReadEnvironmentVariables { get; set; }

    /// <summary>The environment name passed to validation, or <c>null</c> for none, which is production-grade.</summary>
    public string? EnvironmentName { get; set; }
}

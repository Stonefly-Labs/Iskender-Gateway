namespace Iskender.Hosting.Composition;

/// <summary>The options <c>AddIskender(...)</c> copied from <see cref="IskenderOptions"/>, registered as one container instance (research R2).</summary>
internal sealed record IskenderRuntimeOptions(
    IskenderConfigurationDocument? BaseDocument,
    IskenderConfigurationDocument? OverlayDocument,
    bool ReadEnvironmentVariables,
    string? EnvironmentName)
{
    public static IskenderRuntimeOptions From(IskenderOptions options) =>
        new(options.BaseDocument, options.OverlayDocument, options.ReadEnvironmentVariables, options.EnvironmentName);
}

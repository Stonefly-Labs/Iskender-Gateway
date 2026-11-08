namespace Iskender.Core.Contracts.Configuration;

/// <summary>Fixed text forms shared by the configuration types.</summary>
internal static class ConfigurationText
{
    /// <summary>The source of a location with no source, and the text of an absent source name.</summary>
    public const string None = "(none)";

    public static string Layer(ConfigurationLayer layer) => layer switch
    {
        ConfigurationLayer.Defaults => "defaults",
        ConfigurationLayer.Base => "base",
        ConfigurationLayer.Overlay => "overlay",
        ConfigurationLayer.Environment => "environment",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };
}

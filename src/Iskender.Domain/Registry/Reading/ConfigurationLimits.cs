namespace Iskender.Domain.Registry;

/// <summary>The per-document safety bounds of TR-12. They are not performance figures (Constitution VIII).</summary>
internal static class ConfigurationLimits
{
    /// <summary>1 MiB. Reading stops at the byte past it.</summary>
    public const int MaxDocumentBytes = 1_048_576;

    public const int MaxDepth = 32;

    public const int MaxNodes = 10_000;

    /// <summary>64 KiB of UTF-8, counted in bytes before any schema check.</summary>
    public const int MaxStringBytes = 65_536;

    public const int MaxReportedErrors = 100;
}

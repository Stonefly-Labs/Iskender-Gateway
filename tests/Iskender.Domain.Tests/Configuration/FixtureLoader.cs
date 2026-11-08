using System.Text.Json;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>One expected error in a fixture's <c>.expect.json</c> sidecar.</summary>
internal sealed record ExpectedError(string Code, string Path, int Line, int Column, string Layer)
{
    public static ExpectedError Of(ConfigurationFieldError error) =>
        new(error.Code.Id, error.Path, error.Location.Line, error.Location.Column, LayerText(error.Location.Layer));

    public static string LayerText(ConfigurationLayer layer) => layer switch
    {
        ConfigurationLayer.Defaults => "defaults",
        ConfigurationLayer.Base => "base",
        ConfigurationLayer.Overlay => "overlay",
        _ => "environment",
    };
}

/// <summary>Loads fixtures from <c>Configuration/Fixtures</c> and checks reports against their sidecars (FR-035).</summary>
internal static class FixtureLoader
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "Configuration", "Fixtures");

    public static string FullPath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The fixture as a base document, named by its file name, in the format its extension names.</summary>
    public static ConfigurationDocumentSource Document(string relative) =>
        ConfigurationDocumentSource.FromFile(FullPath(relative), FormatOf(relative));

    public static ConfigurationDocumentFormat FormatOf(string path) =>
        path.EndsWith(".json", StringComparison.Ordinal) ? ConfigurationDocumentFormat.Json : ConfigurationDocumentFormat.Yaml;

    /// <summary>The sidecar of <paramref name="relative"/>: <c>&lt;name&gt;.expect.json</c> beside it.</summary>
    public static IReadOnlyList<ExpectedError> Expected(string relative)
    {
        var sidecar = SidecarOf(FullPath(relative));
        using var document = JsonDocument.Parse(File.ReadAllBytes(sidecar));
        return document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => new ExpectedError(
                error.GetProperty("code").GetString()!,
                error.GetProperty("path").GetString()!,
                error.GetProperty("line").GetInt32(),
                error.GetProperty("column").GetInt32(),
                error.GetProperty("layer").GetString()!))
            .ToList();
    }

    public static string SidecarOf(string fixturePath) =>
        Path.Combine(Path.GetDirectoryName(fixturePath)!, Path.GetFileNameWithoutExtension(fixturePath) + ".expect.json");

    /// <summary>Every fixture file under <paramref name="folder"/>, sidecars and READMEs excluded, as paths relative to the root.</summary>
    public static IReadOnlyList<string> Fixtures(string folder) =>
        Directory.Exists(FullPath(folder))
            ? Directory.EnumerateFiles(FullPath(folder), "*", SearchOption.AllDirectories)
                .Where(path => (path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal))
                    && !path.EndsWith(".expect.json", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    /// <summary>Asserts the report holds exactly the expected errors, in order, with code, path, line, column, and layer.</summary>
    public static void AssertErrors(IReadOnlyList<ExpectedError> expected, ConfigurationValidationReport report)
    {
        Assert.False(report.IsValid);
        Assert.Null(report.Snapshot);
        Assert.Equal(expected, report.Errors.Select(ExpectedError.Of).ToList());
    }

    /// <summary>Validates the fixture as the base document against the test schemas.</summary>
    public static ConfigurationValidationReport Validate(string relative, string? environmentName = null) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), Document(relative), environmentName: environmentName));

    /// <summary>Validates a negative fixture and checks it against its sidecar.</summary>
    public static ConfigurationValidationReport AssertMatchesSidecar(string relative, string? environmentName = null)
    {
        var report = Validate(relative, environmentName);
        AssertErrors(Expected(relative), report);
        return report;
    }
}

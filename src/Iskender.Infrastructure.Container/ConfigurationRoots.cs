using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// Resolves a container's configuration sources before the host is built (container contract, Configuration roots;
/// research R8). Owned by ISK-0007.
/// </summary>
public static class ConfigurationRoots
{
    /// <summary>The variable that names the configuration directory.</summary>
    public const string DirectoryVariable = "ISKENDER_CONFIG_DIRECTORY";

    /// <summary>The variable that names Iskender's environment.</summary>
    public const string EnvironmentVariable = "ISKENDER_ENVIRONMENT";

    /// <summary>The directory used when <see cref="DirectoryVariable"/> is unset or empty.</summary>
    public const string DefaultDirectory = "/etc/iskender";

    private const string FileStem = "iskender";

    private const int MaxEnvironmentNameLength = 32;

    /// <summary>
    /// The sources for <paramref name="environment"/>: the base and overlay documents found, the process's
    /// <c>ISKENDER__</c> variables, and the environment name. An empty variable counts as unset. Fails with
    /// <c>hosting.configuration_rejected</c>, naming the layer and source name and no content, when the named directory
    /// does not exist or a layer has both a <c>.yaml</c> and a <c>.json</c> file, and with <c>core.value.invalid</c> for a
    /// <c>null</c> argument. Opens no file.
    /// </summary>
    public static CompositionSources Resolve(IReadOnlyDictionary<string, string?> environment, IFileSystemProbe files)
    {
        if (environment is null || files is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var named = Value(environment, DirectoryVariable);
        if (named is not null && !files.DirectoryExists(named))
        {
            throw CompositionConfiguration.Unreadable("directory", DirectoryVariable);
        }

        var directory = named ?? DefaultDirectory;
        var environmentName = Value(environment, EnvironmentVariable);
        var overlay = environmentName is not null && IsEnvironmentName(environmentName)
            ? Layer(files, directory, "overlay", $"{FileStem}.{environmentName}")
            : null;

        return new CompositionSources(Layer(files, directory, "base", FileStem), overlay, true, environmentName);
    }

    private static CompositionDocument? Layer(IFileSystemProbe files, string directory, string layer, string stem)
    {
        var yaml = Document(files, directory, $"{stem}.yaml", ConfigurationDocumentFormat.Yaml);
        var json = Document(files, directory, $"{stem}.json", ConfigurationDocumentFormat.Json);
        return (yaml, json) switch
        {
            ({ }, { }) => throw CompositionConfiguration.Unreadable(layer, $"{stem}.yaml and {stem}.json"),
            _ => yaml ?? json,
        };
    }

    private static CompositionDocument? Document(IFileSystemProbe files, string directory, string name, ConfigurationDocumentFormat format)
    {
        var path = Path.Combine(directory, name);
        return files.FileExists(path) ? CompositionDocument.FromStream(name, format, () => files.OpenRead(path)) : null;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> environment, string name) =>
        environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static bool IsEnvironmentName(string name) =>
        name.Length <= MaxEnvironmentNameLength && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}

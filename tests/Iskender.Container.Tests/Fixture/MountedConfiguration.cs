namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// A temporary configuration directory holding one <c>iskender.json</c>, mounted read-only at <c>/etc/iskender</c>. The
/// directory and file are readable by every user, so the image's user, 1654, can read them on a Linux runner.
/// </summary>
internal sealed class MountedConfiguration : IDisposable
{
    private const UnixFileMode Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("isk-config-");

    public MountedConfiguration(string document)
    {
        var path = Path.Combine(_directory.FullName, "iskender.json");
        File.WriteAllText(path, document);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, Readable);
            File.SetUnixFileMode(_directory.FullName, Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// A document with <see cref="PlaneDefinitions.Persistence"/>'s section, whose host never resolves, and
    /// <see cref="PlaneDefinitions.Container"/>'s.
    /// </summary>
    public static MountedConfiguration Unreachable() => new("{\"iskender\":{" + PlaneDefinitions.Container + "," + PlaneDefinitions.Persistence + "}}");

    /// <summary>The <c>docker run</c> options that mount it.</summary>
    public string[] Mount => ["--volume", $"{_directory.FullName}:/etc/iskender:ro"];

    public void Dispose() => _directory.Delete(recursive: true);
}

namespace Iskender.Infrastructure.Container;

/// <summary>The file system as the configuration roots see it, so tests can supply one in memory. Owned by ISK-0007.</summary>
public interface IFileSystemProbe
{
    /// <summary>True when <paramref name="path"/> names a directory that exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>True when <paramref name="path"/> names a file that exists, matching its name exactly, including case.</summary>
    bool FileExists(string path);

    /// <summary>Opens the file for reading. Called only when the configuration is validated.</summary>
    Stream OpenRead(string path);
}

/// <summary>The process's file system, through <see cref="System.IO"/>. Owned by ISK-0007.</summary>
public sealed class FileSystemProbe : IFileSystemProbe
{
    /// <summary>The single instance.</summary>
    public static FileSystemProbe Instance { get; } = new();

    private FileSystemProbe()
    {
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>
    /// <see cref="File.Exists"/> ignores case on a case-insensitive file system, so the name is also compared with the
    /// directory's entries, ordinally.
    /// </summary>
    public bool FileExists(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(directory)
            || Directory.EnumerateFiles(directory, name).Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal));
    }

    public Stream OpenRead(string path) => File.OpenRead(path);
}

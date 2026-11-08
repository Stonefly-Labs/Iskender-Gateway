namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>Files addressed by repository-relative paths with <c>/</c> separators: the checkout, the base, or a fixture.</summary>
public interface IFileSet
{
    /// <summary>A name for messages, such as a short commit hash or "the checkout".</summary>
    string Name { get; }

    /// <summary>The file's bytes, or null when it is absent.</summary>
    byte[]? Read(string path);

    /// <summary>Every file below <paramref name="directory"/>, recursively, sorted ordinally.</summary>
    IReadOnlyList<string> List(string directory);
}

/// <summary>Files on disk below a root directory.</summary>
public sealed class DirectoryFileSet(string root, string name) : IFileSet
{
    public string Name { get; } = name;

    public byte[]? Read(string path)
    {
        var full = Path.Combine(root, path);
        return File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    public IReadOnlyList<string> List(string directory)
    {
        var full = Path.Combine(root, directory);
        if (!Directory.Exists(full))
        {
            return [];
        }

        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

public static class FileSetExtensions
{
    public static string? ReadText(this IFileSet files, string path) =>
        files.Read(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
}

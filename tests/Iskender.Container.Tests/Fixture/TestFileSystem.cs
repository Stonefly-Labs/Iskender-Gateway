using System.Collections.Concurrent;
using System.Text;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Fixture;

/// <summary>An in-memory <see cref="IFileSystemProbe"/>. Paths match ordinally, so case matters, and every open is recorded.</summary>
internal sealed class TestFileSystem : IFileSystemProbe
{
    private readonly ConcurrentDictionary<string, string?> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _directories = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _opened = new();

    /// <summary>Every path opened, in order.</summary>
    public IReadOnlyList<string> Opened => [.. _opened];

    public TestFileSystem WithDirectory(string path)
    {
        _directories[path] = true;
        return this;
    }

    /// <summary>Adds a file and its directory.</summary>
    public TestFileSystem WithFile(string path, string text)
    {
        _directories[Path.GetDirectoryName(path)!] = true;
        _files[path] = text;
        return this;
    }

    /// <summary>Adds a file that exists but whose open fails with <see cref="UnauthorizedAccessException"/>.</summary>
    public TestFileSystem WithUnreadableFile(string path)
    {
        _directories[Path.GetDirectoryName(path)!] = true;
        _files[path] = null;
        return this;
    }

    public bool DirectoryExists(string path) => _directories.ContainsKey(path);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public Stream OpenRead(string path)
    {
        _opened.Enqueue(path);
        return _files.TryGetValue(path, out var text) && text is not null
            ? new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false)
            : throw new UnauthorizedAccessException("The test file cannot be read.");
    }
}

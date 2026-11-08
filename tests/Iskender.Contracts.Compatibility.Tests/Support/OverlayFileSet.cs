using System.Text;
using Iskender.Contracts.Compatibility.Tests.Harness;

namespace Iskender.Contracts.Compatibility.Tests.Support;

/// <summary>Another file set with some files replaced, added, or (with a null text) deleted, so a test can change one input.</summary>
public sealed class OverlayFileSet(IFileSet inner, string name, IReadOnlyDictionary<string, string?> changes) : IFileSet
{
    public string Name { get; } = name;

    public byte[]? Read(string path)
    {
        if (changes.TryGetValue(path, out var text))
        {
            return text is null ? null : Encoding.UTF8.GetBytes(text);
        }

        return changes.Any(change => change.Value is null && change.Key.EndsWith('/') && path.StartsWith(change.Key, StringComparison.Ordinal)) ? null : inner.Read(path);
    }

    public IReadOnlyList<string> List(string directory)
    {
        var prefix = directory.TrimEnd('/') + "/";
        return inner.List(directory)
            .Concat(changes.Where(change => change.Value is not null && change.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(change => change.Key))
            .Distinct()
            .Where(path => Read(path) is not null)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

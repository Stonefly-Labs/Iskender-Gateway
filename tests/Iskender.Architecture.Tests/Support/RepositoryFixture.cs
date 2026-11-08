using System.Text.Json;

namespace Iskender.Architecture.Tests.Support;

/// <summary>
/// A temp copy of every real input a rule reads, for one mutation (research R8). Starting from the real files
/// proves that the mutation alone caused the violation. Nothing tracked ever contains a violating project file.
/// </summary>
public sealed class RepositoryFixture : IDisposable
{
    private static readonly string[] RootFiles =
    [
        "Iskender.slnx",
        "Directory.Build.props",
        "Directory.Packages.props",
        "Directory.Build.targets",
        "global.json",
        ".editorconfig",
        ".iskender/grounding.yaml",
        ".github/CODEOWNERS",
        ".github/workflows/ci.yml",
        ".github/scripts/docs-gate.sh",
        "CONTRIBUTING.md",
        "tests/Iskender.Architecture.Tests/module-dependency-rules.json",
    ];

    private RepositoryFixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    /// <summary>Copies the real repository's inputs to a new temp directory.</summary>
    public static RepositoryFixture Copy()
    {
        var source = RepositoryPaths.Root();
        var fixture = new RepositoryFixture(Directory.CreateTempSubdirectory("iskender-fixture-").FullName);

        foreach (var file in RootFiles.Concat(GroundedFiles(source)))
        {
            fixture.CopyFrom(source, file);
        }

        foreach (var file in RepositoryPaths.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            var relative = RepositoryPaths.Relative(source, file);
            var isSource = name.EndsWith(".cs", StringComparison.Ordinal)
                && (relative.StartsWith("src/", StringComparison.Ordinal) || relative.StartsWith("tests/", StringComparison.Ordinal));
            if (RepositoryPaths.IsProjectFile(file) || name == "packages.lock.json" || isSource)
            {
                fixture.CopyFrom(source, relative);
            }
        }

        return fixture;
    }

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public string Read(string relative) => File.ReadAllText(PathOf(relative));

    public void Write(string relative, string content)
    {
        var path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Delete(string relative) => File.Delete(PathOf(relative));

    /// <summary>Replaces text that must exist, so that a stale fixture fails rather than proving nothing.</summary>
    public void Replace(string relative, string oldText, string newText)
    {
        var content = Read(relative);
        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{oldText}' is not in {relative}.");
        }

        Write(relative, content.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>Adds XML just before the closing Project tag of a build file.</summary>
    public void AddToProject(string relative, string xml) =>
        Replace(relative, "</Project>", $"{xml}\n</Project>");

    public void Dispose() => Directory.Delete(Root, recursive: true);

    /// <summary>The contract and the docs/03 table's file, found the way the rules find them.</summary>
    private static IEnumerable<string> GroundedFiles(string source)
    {
        using var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "tests/Iskender.Architecture.Tests/module-dependency-rules.json")));
        var grounding = GroundingManifest.Read(source, "grounding", "root");
        var specifications = GroundingManifest.Read(source, "speckit", "specificationsRoot");

        yield return $"{grounding}/{rules.RootElement.GetProperty("moduleTable").GetProperty("file").GetString()}";
        yield return $"{specifications}/{rules.RootElement.GetProperty("contract").GetProperty("path").GetString()}";
    }

    private void CopyFrom(string source, string relative)
    {
        var from = Path.Combine(source, relative);
        if (File.Exists(from))
        {
            var to = PathOf(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
        }
    }
}

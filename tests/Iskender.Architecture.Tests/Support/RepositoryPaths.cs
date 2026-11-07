namespace Iskender.Architecture.Tests.Support;

public static class RepositoryPaths
{
    /// <summary>Directories the repository scan never enters (research R5). All of them are ignored by Git.</summary>
    private static readonly HashSet<string> SkippedDirectories =
        new(["bin", "obj", "artifacts", "TestResults", ".git", "node_modules"], StringComparer.Ordinal);

    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Iskender.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Iskender.sln above the test output directory.");
    }

    public static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Every file below <paramref name="directory"/>, skipping the scan's skipped directories.</summary>
    public static IEnumerable<string> EnumerateFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>True for a project file: any extension ending in "proj", such as .csproj or .proj.</summary>
    public static bool IsProjectFile(string path) =>
        Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase);
}

namespace Iskender.Contracts.Compatibility.Tests.Support;

public static class Repository
{
    /// <summary>The harness project, relative to the repository root.</summary>
    public const string HarnessDirectory = "tests/Iskender.Contracts.Compatibility.Tests";

    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Iskender.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Iskender.slnx above the test output directory.");
    }
}

using System.Reflection;

namespace Iskender.Core.Contracts.Tests;

public class SolutionBuildsTests
{
    private static string RepoRoot()
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

    [Fact]
    public void CoreContractsAssemblyLoads()
    {
        Assert.Equal("Iskender.Core.Contracts", Assembly.Load("Iskender.Core.Contracts").GetName().Name);
    }

    [Fact]
    public void SolutionRegistersEveryProjectOnDisk()
    {
        var root = RepoRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Iskender.sln"));

        var projects = new[] { "src", "tests" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(projects);
        Assert.All(projects, project => Assert.Contains(project, solution, StringComparison.Ordinal));
    }

    [Fact]
    public void SkeletonContainsNoGatewayBusinessCode()
    {
        var root = RepoRoot();

        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["src/Iskender.Gateway.Host/Program.cs"], sources);
        Assert.DoesNotContain("Map", File.ReadAllText(Path.Combine(root, "src", "Iskender.Gateway.Host", "Program.cs")), StringComparison.Ordinal);
    }
}

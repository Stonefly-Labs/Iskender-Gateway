using System.Reflection;
using NetArchTest.Rules;

namespace Iskender.Architecture.Tests;

public class DependencyDirectionTests
{
    private static readonly string[] SkeletonAssemblies =
    [
        "Iskender.Core.Contracts",
        "Iskender.Domain",
        "Iskender.Infrastructure",
        "Iskender.Providers.Abstractions",
        "Iskender.Policy.Sdk",
        "Iskender.Management",
    ];

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

    private static ProjectGraph Graph(params (string Project, string[] References)[] edges) =>
        new(edges.ToDictionary(edge => edge.Project, edge => (IReadOnlyCollection<string>)edge.References));

    [Fact]
    public void RepositoryProjectGraphHasNoViolations()
    {
        var graph = ProjectGraph.Load(Path.Combine(RepoRoot(), "src"));

        Assert.Empty(graph.Violations());
    }

    [Fact]
    public void RepositoryProjectGraphContainsEverySkeletonProject()
    {
        var graph = ProjectGraph.Load(Path.Combine(RepoRoot(), "src"));

        foreach (var project in SkeletonAssemblies.Append("Iskender.Gateway.Host"))
        {
            Assert.Contains(project, graph.Edges.Keys);
        }
    }

    [Fact]
    public void HostComposesEverySkeletonProject()
    {
        var references = ProjectGraph.Load(Path.Combine(RepoRoot(), "src")).Edges["Iskender.Gateway.Host"];

        Assert.All(SkeletonAssemblies, project => Assert.Contains(project, references));
    }

    [Fact]
    public void A1ViolationFails()
    {
        var violations = Graph(("Iskender.Core.Contracts", ["Iskender.Domain"]), ("Iskender.Domain", [])).Violations();

        Assert.Contains(violations, violation => violation.StartsWith("A1:", StringComparison.Ordinal));
    }

    [Fact]
    public void A2ViolationFails()
    {
        var violations = Graph(("Iskender.Domain", ["Iskender.Gateway.Host"]), ("Iskender.Gateway.Host", [])).Violations();

        Assert.Contains(violations, violation => violation.StartsWith("A2:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Management")]
    public void A3ViolationFails(string project)
    {
        var violations = Graph((project, ["Iskender.Infrastructure"]), ("Iskender.Infrastructure", [])).Violations();

        Assert.Contains(violations, violation => violation.StartsWith("A3:", StringComparison.Ordinal));
    }

    [Fact]
    public void A4CycleFails()
    {
        var violations = Graph(("Iskender.Domain", ["Iskender.Management"]), ("Iskender.Management", ["Iskender.Domain"])).Violations();

        Assert.Contains(violations, violation => violation.StartsWith("A4:", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadParsesWindowsStyleReferencePaths()
    {
        var root = Directory.CreateTempSubdirectory("iskender-graph-");
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "Iskender.Core.Contracts"));
            File.WriteAllText(
                Path.Combine(project.FullName, "Iskender.Core.Contracts.csproj"),
                """<Project><ItemGroup><ProjectReference Include="..\Iskender.Domain\Iskender.Domain.csproj" /></ItemGroup></Project>""");

            var violations = ProjectGraph.Load(Path.Combine(root.FullName, "src")).Violations();

            Assert.Contains(violations, violation => violation.StartsWith("A1:", StringComparison.Ordinal));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CoreContractsHasNoTypeLevelInternalDependencies()
    {
        var result = Types.InAssembly(Assembly.Load("Iskender.Core.Contracts"))
            .ShouldNot()
            .HaveDependencyOnAny("Iskender.Domain", "Iskender.Infrastructure", "Iskender.Providers", "Iskender.Policy", "Iskender.Management", "Iskender.Gateway")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Management")]
    public void AbstractionsHaveNoTypeLevelDependencyOnInfrastructureOrHost(string assembly)
    {
        var result = Types.InAssembly(Assembly.Load(assembly))
            .ShouldNot()
            .HaveDependencyOnAny("Iskender.Infrastructure", "Iskender.Gateway.Host")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}

using System.Reflection;
using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;
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
        "Iskender.Hosting",
    ];

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    private static ProjectGraph.Node Production(string project, params string[] references) => new(project, false, references);

    private static ProjectGraph.Node TestProject(string project, params string[] references) => new(project, true, references);

    private static ProjectGraph RealGraph() =>
        ProjectGraph.From(Source, RepositoryScan.Run(RepositoryPaths.Root()).Projects);

    private static IReadOnlyList<Violation> Evaluate(params ProjectGraph.Node[] nodes) =>
        GraphRules.Evaluate(Source, new ProjectGraph(Source, nodes));

    [Fact]
    public void RepositoryHasNoViolations()
    {
        Assert.Empty(RuleRunner.Run(RepositoryPaths.Root()).Select(violation => violation.Message));
    }

    [Fact]
    public void RepositoryProjectGraphIncludesTestProjects()
    {
        Assert.True(RealGraph().Contains("Iskender.Architecture.Tests"));
    }

    [Fact]
    public void RepositoryProjectGraphContainsEverySkeletonProject()
    {
        var graph = RealGraph();

        foreach (var project in SkeletonAssemblies.Append("Iskender.Gateway.Host").Append("Iskender.ControlPlane.Host"))
        {
            Assert.True(graph.Contains(project), project);
        }
    }

    [Fact]
    public void EachHostComposesWhatItsRoleRequiresAndStaysOutOfTheOtherPlane()
    {
        var graph = RealGraph();

        Assert.Equal(2, Source.RequiredReferences.Count);
        foreach (var (host, required) in Source.RequiredReferences)
        {
            Assert.Equal(2, required.Count);
            Assert.All(required, reference => Assert.Contains(reference, graph.ReferencesOf(host)));
        }

        var violations = GraphRules.Evaluate(Source, graph);
        Assert.DoesNotContain(violations, violation => violation.RuleId is "A6" or "A7" or "A12");
        Assert.Contains(Source.ForbiddenReach, entry => entry.Rule == "A6");
        Assert.Contains(Source.ForbiddenReach, entry => entry.Rule == "A7");
    }

    [Fact]
    [ProvesRule("A1")]
    public void A1ViolationFails()
    {
        var violations = Evaluate(Production("Iskender.Core.Contracts", "Iskender.Domain"), Production("Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A1");
    }

    [Fact]
    [ProvesRule("A2")]
    public void A2ViolationFails()
    {
        var violations = Evaluate(Production("Iskender.Domain", "Iskender.Gateway.Host"), Production("Iskender.Gateway.Host"));

        Assert.Contains(violations, violation => violation.RuleId == "A2");
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Management")]
    [ProvesRule("A3")]
    public void A3ViolationFails(string project)
    {
        var violations = Evaluate(Production(project, "Iskender.Infrastructure"), Production("Iskender.Infrastructure"));

        Assert.Contains(violations, violation => violation.RuleId == "A3");
    }

    [Fact]
    [ProvesRule("A4")]
    public void A4CycleFails()
    {
        var violations = Evaluate(Production("Iskender.Domain", "Iskender.Management"), Production("Iskender.Management", "Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A4");
    }

    [Fact]
    [ProvesRule("A2")]
    public void A2AppliesToTestProjects()
    {
        var violations = Evaluate(TestProject("Iskender.Domain.Tests", "Iskender.Gateway.Host"), Production("Iskender.Gateway.Host"));

        Assert.Contains(violations, violation => violation.RuleId == "A2" && violation.Source == "Iskender.Domain.Tests");
    }

    [Theory]
    [InlineData("Iskender.Domain", "Iskender.Management")]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.Policy.Sdk")]
    [InlineData("Iskender.Management", "Iskender.Infrastructure")]
    [ProvesRule("A5")]
    public void A5UnapprovedEdgeFails(string project, string reference)
    {
        var violations = Evaluate(Production(project, reference), Production(reference));

        Assert.Contains(
            violations,
            violation => violation.Message == $"A5: {project} -> {reference}: not an approved reference. See {Source.ContractPath} (version {Source.ContractVersion}).");
    }

    [Fact]
    [ProvesRule("A5")]
    public void A5UnknownProductionProjectFails()
    {
        var violations = Evaluate(Production("Iskender.Unplanned"));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Unplanned");
    }

    [Fact]
    public void A5AllowsTestProjectsToReferenceWhatTheyTest()
    {
        var approved = Source.Projects
            .Where(project => !RuleSource.IsPattern(project.Name))
            .Select(project => Production(project.Name, [.. project.References.Where(reference => !RuleSource.IsPattern(reference))]));

        var violations = Evaluate([.. approved, TestProject("Iskender.Domain.Tests", "Iskender.Domain")]);

        Assert.Empty(violations.Select(violation => violation.Message));
    }

    [Fact]
    public void LoadParsesWindowsStyleReferencePaths()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(
            "src/Iskender.Core.Contracts/Iskender.Core.Contracts.csproj",
            """<ItemGroup><ProjectReference Include="..\Iskender.Domain\Iskender.Domain.csproj" /></ItemGroup>""");

        Assert.Contains(RuleRunner.Run(fixture.Root), violation => violation.RuleId == "A1" && violation.Target == "Iskender.Domain");
    }

    [Fact]
    public void CoreContractsHasNoTypeLevelInternalDependencies()
    {
        var result = Types.InAssembly(Assembly.Load("Iskender.Core.Contracts"))
            .ShouldNot()
            .HaveDependencyOnAny("Iskender.Domain", "Iskender.Infrastructure", "Iskender.Providers", "Iskender.Policy", "Iskender.Management", "Iskender.Gateway", "Iskender.Hosting", "Iskender.ControlPlane")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Providers.Abstractions")]
    [InlineData("Iskender.Policy.Sdk")]
    [InlineData("Iskender.Management")]
    [InlineData("Iskender.Hosting")]
    public void AbstractionsHaveNoTypeLevelDependencyOnInfrastructureOrHost(string assembly)
    {
        var result = Types.InAssembly(Assembly.Load(assembly))
            .ShouldNot()
            .HaveDependencyOnAny("Iskender.Infrastructure", "Iskender.Gateway.Host", "Iskender.ControlPlane")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}

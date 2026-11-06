using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class GraphRuleTests
{
    private const string Adapter = "Iskender.Providers.Foo";

    private const string InfrastructureAdapter = "Iskender.Infrastructure.Bar";

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    private static string Contract => $"See {Source.ContractPath} (version {Source.ContractVersion}).";

    public static TheoryData<string, string> EveryUnapprovedEdge
    {
        get
        {
            var projects = Source.Projects
                .Where(project => !RuleSource.IsPattern(project.Name))
                .Select(project => project.Name)
                .Concat([Adapter, InfrastructureAdapter])
                .ToList();

            var data = new TheoryData<string, string>();
            foreach (var from in projects)
            {
                var definition = Source.Resolve(from)!;
                foreach (var to in projects.Where(to => to != from && !definition.References.Any(entry => Source.Matches(entry, to))))
                {
                    data.Add(from, to);
                }
            }

            return data;
        }
    }

    private static ProjectGraph.Node Production(string project, params string[] references) => new(project, false, references);

    private static ProjectGraph.Node TestProject(string project, params string[] references) => new(project, true, references);

    private static IReadOnlyList<Violation> Evaluate(params ProjectGraph.Node[] nodes) =>
        GraphRules.Evaluate(Source, new ProjectGraph(Source, nodes));

    private static IReadOnlyList<Violation> RunWithReference(string project, string target)
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(project, $"""<ItemGroup><ProjectReference Include="{target}" /></ItemGroup>""");
        return RuleRunner.Run(fixture.Root);
    }

    private static string Src(string name) => $"src/{name}/{name}.csproj";

    private static string Up(string name) => $"../{name}/{name}.csproj";

    [Fact]
    [ProvesRule("A1")]
    public void A1ReportsTheWholeMessage()
    {
        var violations = Evaluate(Production("Iskender.Core.Contracts", "Iskender.Domain"), Production("Iskender.Domain"));

        Assert.Contains(
            $"A1: Iskender.Core.Contracts -> Iskender.Domain: must reference no other Iskender project. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Fact]
    [ProvesRule("A1")]
    public void A1FailsOnDisk()
    {
        var violations = RunWithReference(Src("Iskender.Core.Contracts"), Up("Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A1" && violation.Source == "Iskender.Core.Contracts");
    }

    [Fact]
    [ProvesRule("A2")]
    public void A2ReportsTheWholeMessageAndThePath()
    {
        var violations = Evaluate(Production("Iskender.Domain", "Iskender.Hosting"), Production("Iskender.Hosting", "Iskender.ControlPlane.Host"), Production("Iskender.ControlPlane.Host"));

        Assert.Contains(
            $"A2: Iskender.Domain -> Iskender.Hosting -> Iskender.ControlPlane.Host: must not reach this project. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData("Iskender.ControlPlane.Host")]
    [ProvesRule("A2")]
    public void A2FailsOnDiskForEveryHost(string host)
    {
        var violations = RunWithReference(Src("Iskender.Domain"), Up(host));

        Assert.Contains(violations, violation => violation.RuleId == "A2" && violation.Source == "Iskender.Domain" && violation.Target == host);
    }

    [Fact]
    [ProvesRule("A2")]
    public void A2FailsOnDiskForATestProject()
    {
        var violations = RunWithReference("tests/Iskender.Core.Contracts.Tests/Iskender.Core.Contracts.Tests.csproj", "../../src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj");

        Assert.Contains(violations, violation => violation.RuleId == "A2" && violation.Source == "Iskender.Core.Contracts.Tests");
    }

    [Theory]
    [InlineData("Iskender.Domain", "Iskender.Infrastructure")]
    [InlineData("Iskender.Domain", InfrastructureAdapter)]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.Infrastructure")]
    [InlineData(Adapter, "Iskender.Infrastructure")]
    [InlineData("Iskender.Policy.Sdk", InfrastructureAdapter)]
    [InlineData("Iskender.Management", "Iskender.Infrastructure")]
    [ProvesRule("A3")]
    public void A3FailsForEachListedProject(string project, string target)
    {
        var violations = Evaluate(Production(project, target), Production(target));

        Assert.Contains(
            $"A3: {project} -> {target}: must not reach this project. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Fact]
    [ProvesRule("A3")]
    public void A3FailsOnDiskUnderANeverTrueCondition()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(Src("Iskender.Management"), """<ItemGroup Condition="'$(NeverTrue)' == 'true'"><ProjectReference Include="..\Iskender.Infrastructure\Iskender.Infrastructure.csproj" /></ItemGroup>""");

        var messages = RuleRunner.Run(fixture.Root).Select(violation => violation.Message).ToList();

        Assert.Contains($"A3: Iskender.Management -> Iskender.Infrastructure: must not reach this project. {Contract}", messages);
        Assert.Contains($"A5: Iskender.Management -> Iskender.Infrastructure: not an approved reference. {Contract}", messages);
    }

    [Fact]
    [ProvesRule("A4")]
    public void A4ReportsTheWholeMessage()
    {
        var violations = Evaluate(Production("Iskender.Management", "Iskender.Domain"), Production("Iskender.Domain", "Iskender.Management"));

        Assert.Contains(
            $"A4: Iskender.Domain -> Iskender.Management -> Iskender.Domain: is a reference cycle. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Fact]
    [ProvesRule("A4")]
    public void A4FailsOnDisk()
    {
        var violations = RunWithReference(Src("Iskender.Domain"), Up("Iskender.Infrastructure"));

        Assert.Contains(violations, violation => violation.RuleId == "A4");
    }

    [Fact]
    [ProvesRule("A4")]
    public void A4ReportsASelfReference()
    {
        var violations = Evaluate(Production("Iskender.Domain", "Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A4" && violation.Source == "Iskender.Domain" && violation.Target == "Iskender.Domain");
    }

    [Fact]
    public void ANoCycleGraphReportsNoA4()
    {
        var violations = Evaluate(Production("Iskender.Domain", "Iskender.Core.Contracts"), Production("Iskender.Hosting", "Iskender.Domain", "Iskender.Core.Contracts"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A4");
    }

    [Theory]
    [MemberData(nameof(EveryUnapprovedEdge))]
    [ProvesRule("A5")]
    public void A5RejectsEveryEdgeOutsideTheAllowList(string project, string target)
    {
        var violations = Evaluate(Production(project, target), Production(target));

        Assert.Contains($"A5: {project} -> {target}: not an approved reference. {Contract}", violations.Select(violation => violation.Message));
    }

    [Fact]
    public void A5EnumerationCoversBothPatterns()
    {
        var edges = EveryUnapprovedEdge.Select(row => row.Data).ToList();

        Assert.Contains((Adapter, "Iskender.Domain"), edges);
        Assert.Contains((InfrastructureAdapter, Adapter), edges);
        Assert.Contains(("Iskender.Gateway.Host", "Iskender.Management"), edges);
        Assert.DoesNotContain(("Iskender.Gateway.Host", Adapter), edges);
        Assert.DoesNotContain(("Iskender.ControlPlane.Host", InfrastructureAdapter), edges);
    }

    [Fact]
    [ProvesRule("A5")]
    public void A5FailsOnDisk()
    {
        var violations = RunWithReference(Src("Iskender.Providers.Abstractions"), Up("Iskender.Policy.Sdk"));

        Assert.Contains($"A5: Iskender.Providers.Abstractions -> Iskender.Policy.Sdk: not an approved reference. {Contract}", violations.Select(violation => violation.Message));
    }

    [Fact]
    [ProvesRule("A5")]
    public void AProviderAdapterReferencingTheDomainFails()
    {
        var violations = Evaluate(Production(Adapter, "Iskender.Domain"), Production("Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == Adapter && violation.Target == "Iskender.Domain");
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Infrastructure")]
    [ProvesRule("A5")]
    public void ThePolicySdkReferencingAnInternalProjectFails(string target)
    {
        var violations = Evaluate(Production("Iskender.Policy.Sdk", target), Production(target));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Policy.Sdk" && violation.Target == target);
    }

    [Fact]
    [ProvesRule("A5")]
    public void ManagementReferencingInfrastructureFails()
    {
        var violations = Evaluate(Production("Iskender.Management", "Iskender.Infrastructure"), Production("Iskender.Infrastructure"));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Management");
        Assert.Contains(violations, violation => violation.RuleId == "A3" && violation.Source == "Iskender.Management");
    }

    [Fact]
    [ProvesRule("A5")]
    public void AProviderNameWithTwoSegmentsMatchesNoPattern()
    {
        var violations = Evaluate(Production("Iskender.Providers.Foo.Bar"));

        Assert.Contains(
            $"A5: Iskender.Providers.Foo.Bar -> (approved references): is not listed and matches no pattern. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Fact]
    public void AnAdapterMayReferenceWhatItsPatternApproves()
    {
        var violations = Evaluate(Production(Adapter, "Iskender.Providers.Abstractions", "Iskender.Core.Contracts"), Production(InfrastructureAdapter, "Iskender.Infrastructure", "Iskender.Domain", "Iskender.Core.Contracts"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A5" && violation.Source is Adapter or InfrastructureAdapter);
    }

    [Fact]
    public void TheExemptionDependsOnLocationAlone()
    {
        var violations = Evaluate(Production("Iskender.Escaped.Tests", "Iskender.Domain"), TestProject("Iskender.Anything", "Iskender.Domain"), Production("Iskender.Domain"));

        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Escaped.Tests");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Anything");
    }

    [Fact]
    public void AReferenceOutsideTheScanStaysVisible()
    {
        var graph = new ProjectGraph(Source, [Production("Iskender.Domain", "Iskender.Absent")]);

        Assert.Equal(["Iskender.Absent"], graph.Reach("Iskender.Domain").Keys);
        Assert.Contains(GraphRules.Evaluate(Source, graph), violation => violation.RuleId == "A5" && violation.Target == "Iskender.Absent");
    }

    [Fact]
    public void AReferenceResolvesIgnoringCase()
    {
        var graph = new ProjectGraph(Source, [Production("Iskender.Hosting", "ISKENDER.DOMAIN"), Production("Iskender.Domain")]);

        Assert.Equal(["Iskender.Domain"], graph.ReferencesOf("Iskender.Hosting"));
    }

    [Fact]
    public void AWalkToleratesCyclesAndNeverCountsItsOwnStart()
    {
        var graph = new ProjectGraph(Source, [Production("a", "b"), Production("b", "c"), Production("c", "a")]);

        Assert.Equal(["b", "c"], graph.Reach("a").Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADirectEdgeIsNeverReportedAsALongerPath()
    {
        var graph = new ProjectGraph(Source, [Production("a", "b", "c"), Production("b", "c"), Production("c")]);

        Assert.Equal(["c"], graph.Reach("a")["c"]);
    }

    [Fact]
    [ProvesRule("A5")]
    public void SeveralViolationsAreAllReportedInStableOrderInOneList()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.AddToProject(Src("Iskender.Management"), $"""<ItemGroup><ProjectReference Include="{Up("Iskender.Infrastructure")}" /></ItemGroup>""");
        fixture.AddToProject(Src("Iskender.Core.Contracts"), $"""<ItemGroup><ProjectReference Include="{Up("Iskender.Policy.Sdk")}" /></ItemGroup>""");

        var first = RuleRunner.Run(fixture.Root).Select(violation => violation.Message).ToList();
        var second = RuleRunner.Run(fixture.Root).Select(violation => violation.Message).ToList();

        Assert.Equal(first, second);
        Assert.Contains(first, message => message.StartsWith("A1: Iskender.Core.Contracts -> Iskender.Policy.Sdk:", StringComparison.Ordinal));
        Assert.Contains(first, message => message.StartsWith("A3: Iskender.Management -> Iskender.Infrastructure:", StringComparison.Ordinal));
        Assert.Contains(first, message => message.StartsWith("A5: Iskender.Management -> Iskender.Infrastructure:", StringComparison.Ordinal));
        Assert.True(
            first.FindIndex(message => message.StartsWith("A1:", StringComparison.Ordinal))
            < first.FindIndex(message => message.StartsWith("A3:", StringComparison.Ordinal)));
    }
}

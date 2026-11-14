using Iskender.Architecture.Tests.Rules;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public class GraphRuleTests
{
    private const string Adapter = "Iskender.Providers.Foo";

    private const string InfrastructureAdapter = "Iskender.Infrastructure.Bar";

    private const string Sample = "Iskender.Samples.EmbeddedHost";

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

    private static void AssertReachedThroughIntermediate(string rule, string project, string intermediate, string target)
    {
        var violations = Evaluate(Production(project, intermediate), Production(intermediate, target), Production(target));

        Assert.Contains(
            $"{rule}: {project} -> {intermediate} -> {target}: must not reach this project. {Contract}",
            violations.Select(violation => violation.Message));
    }

    [Theory]
    [InlineData("Iskender.Hosting", "Iskender.Management")]
    [InlineData("Iskender.Hosting", "Iskender.ControlPlane.Host")]
    [ProvesRule("A6")]
    public void A6DataPlaneHostFailsThroughAnIntermediate(string intermediate, string target)
    {
        AssertReachedThroughIntermediate("A6", "Iskender.Gateway.Host", intermediate, target);
    }

    [Theory]
    [InlineData("Iskender.Management", "Iskender.Hosting")]
    [InlineData("Iskender.Management", "Iskender.Gateway.Host")]
    [ProvesRule("A7")]
    public void A7ControlPlaneHostFailsThroughAnIntermediate(string intermediate, string target)
    {
        AssertReachedThroughIntermediate("A7", "Iskender.ControlPlane.Host", intermediate, target);
    }

    [Theory]
    [InlineData("Iskender.Domain", "Iskender.Infrastructure")]
    [InlineData("Iskender.Domain", InfrastructureAdapter)]
    [InlineData("Iskender.Domain", "Iskender.Management")]
    [ProvesRule("A8")]
    public void A8HostingFailsThroughAnIntermediate(string intermediate, string target)
    {
        AssertReachedThroughIntermediate("A8", "Iskender.Hosting", intermediate, target);
    }

    [Theory]
    [InlineData("Iskender.Domain", "Iskender.Management")]
    [InlineData("Iskender.Domain", "Iskender.Hosting")]
    [InlineData("Iskender.Domain", "Iskender.Gateway.Host")]
    [InlineData("Iskender.Domain", "Iskender.ControlPlane.Host")]
    [InlineData("Iskender.Policy.Sdk", "Iskender.Management")]
    [InlineData("Iskender.Policy.Sdk", "Iskender.Hosting")]
    [InlineData("Iskender.Policy.Sdk", "Iskender.Gateway.Host")]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.Management")]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.Hosting")]
    [InlineData("Iskender.Providers.Abstractions", "Iskender.ControlPlane.Host")]
    [InlineData(Adapter, "Iskender.Management")]
    [InlineData(Adapter, "Iskender.Hosting")]
    [InlineData(Adapter, "Iskender.Gateway.Host")]
    [ProvesRule("A9")]
    public void A9ContractProjectsFailThroughAnIntermediate(string project, string target)
    {
        AssertReachedThroughIntermediate("A9", project, "Iskender.Core.Contracts", target);
    }

    [Theory]
    [InlineData("Iskender.Domain")]
    [InlineData("Iskender.Hosting")]
    [ProvesRule("A10")]
    public void A10ProviderAdapterFailsThroughAnIntermediate(string project)
    {
        AssertReachedThroughIntermediate("A10", project, "Iskender.Providers.Abstractions", Adapter);
    }

    [Fact]
    [ProvesRule("A11")]
    public void A11InfrastructureFailsThroughAnIntermediate()
    {
        AssertReachedThroughIntermediate("A11", "Iskender.Infrastructure", "Iskender.Domain", InfrastructureAdapter);
    }

    [Fact]
    [ProvesRule("A3")]
    public void A3FailsTransitivelyThroughAnAdapter()
    {
        AssertReachedThroughIntermediate("A3", "Iskender.Domain", Adapter, "Iskender.Infrastructure");
    }

    [Theory]
    [InlineData("Iskender.Hosting", "Iskender.Domain")]
    [InlineData("Iskender.ControlPlane.Host", "Iskender.Management")]
    [ProvesRule("A4")]
    public void A4CycleThroughEachNewProjectFails(string project, string other)
    {
        var violations = Evaluate(Production(project, other), Production(other, project));

        Assert.Contains(violations, violation =>
            violation.RuleId == "A4"
            && $"{violation.Source} -> {violation.Target}".Contains(project, StringComparison.Ordinal)
            && $"{violation.Source} -> {violation.Target}".Contains(other, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Iskender.Gateway.Host", "Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host", "Iskender.Infrastructure")]
    [InlineData("Iskender.ControlPlane.Host", "Iskender.Management")]
    [InlineData("Iskender.ControlPlane.Host", "Iskender.Infrastructure")]
    [ProvesRule("A12")]
    public void A12FailsForEachRequiredReferenceGoingMissing(string host, string reference)
    {
        var nodes = Source.RequiredReferences
            .Select(entry => Production(entry.Key, [.. entry.Value.Where(value => !(entry.Key == host && value == reference))]))
            .ToArray();

        var violations = Evaluate(nodes).Where(violation => violation.RuleId == "A12").ToList();

        Assert.Equal(
            $"A12: {host} -> {reference}: is a required direct reference. {Contract}",
            Assert.Single(violations).Message);
    }

    [Theory]
    [InlineData("src/Iskender.Gateway.Host/Iskender.Gateway.Host.csproj", "Iskender.Hosting", "Iskender.Gateway.Host")]
    [InlineData("src/Iskender.ControlPlane.Host/Iskender.ControlPlane.Host.csproj", "Iskender.Management", "Iskender.ControlPlane.Host")]
    [ProvesRule("A12")]
    public void A12FailsOnDisk(string project, string reference, string host)
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Replace(project, $"""    <ProjectReference Include="..\{reference}\{reference}.csproj" />""", string.Empty);

        Assert.Contains(RuleRunner.Run(fixture.Root), violation => violation.RuleId == "A12" && violation.Source == host && violation.Target == reference);
    }

    [Fact]
    public void TheDataPlaneHostReachesNoControlPlaneProject()
    {
        var reached = ProjectGraph.From(Source, RepositoryScan.Run(RepositoryPaths.Root()).Projects).Reach("Iskender.Gateway.Host").Keys;

        Assert.Contains("Iskender.Hosting", reached);
        Assert.Empty(reached.Intersect(["Iskender.Management", "Iskender.ControlPlane.Host"]));
    }

    [Fact]
    public void TheControlPlaneHostReachesNoIngressProject()
    {
        var reached = ProjectGraph.From(Source, RepositoryScan.Run(RepositoryPaths.Root()).Projects).Reach("Iskender.ControlPlane.Host").Keys;

        Assert.Contains("Iskender.Management", reached);
        Assert.Empty(reached.Intersect(["Iskender.Hosting", "Iskender.Gateway.Host"]));
    }

    [Fact]
    [ProvesRule("A2")]
    public void A2AppliesToATestProjectReachingTheControlPlaneHost()
    {
        var violations = Evaluate(TestProject("Iskender.Something.Tests", "Iskender.ControlPlane.Host"), Production("Iskender.ControlPlane.Host"));

        Assert.Contains(violations, violation => violation.RuleId == "A2" && violation.Source == "Iskender.Something.Tests" && violation.Target == "Iskender.ControlPlane.Host");
    }

    [Theory]
    [InlineData("Iskender.Infrastructure")]
    [InlineData("Iskender.Hosting")]
    [InlineData("Iskender.Gateway.Host")]
    [InlineData(InfrastructureAdapter)]
    [ProvesRule("A20")]
    public void A20AProductionProjectReachingASampleFails(string project)
    {
        var violations = Evaluate(Production(project, Sample), Production(Sample));

        Assert.Contains(violations, violation => violation.RuleId == "A20" && violation.Source == project && violation.Target == Sample);
    }

    [Fact]
    [ProvesRule("A20")]
    public void A20ASampleReachedThroughAnIntermediateFails()
    {
        AssertReachedThroughIntermediate("A20", "Iskender.Domain", "Iskender.Core.Contracts", Sample);
    }

    [Fact]
    public void A20ATestProjectMayReferenceASample()
    {
        var violations = Evaluate(TestProject("Iskender.Hosting.Tests", Sample), Production(Sample, "Iskender.Hosting"), Production("Iskender.Hosting"));

        Assert.DoesNotContain(violations, violation => violation.RuleId == "A20");
    }

    [Fact]
    [ProvesRule("A20")]
    public void A20FailsForAProductionProjectReferencingTheSampleOnDisk()
    {
        using var fixture = RepositoryFixture.Copy();
        fixture.Write("samples/Iskender.Samples.EmbeddedHost/Iskender.Samples.EmbeddedHost.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web" />""");
        fixture.AddToProject(Src("Iskender.Infrastructure"), """<ItemGroup><ProjectReference Include="../../samples/Iskender.Samples.EmbeddedHost/Iskender.Samples.EmbeddedHost.csproj" /></ItemGroup>""");

        var violations = RuleRunner.Run(fixture.Root);

        Assert.Contains(violations, violation => violation.RuleId == "A20" && violation.Source == "Iskender.Infrastructure" && violation.Target == Sample);
        Assert.Contains(violations, violation => violation.RuleId == "A5" && violation.Source == "Iskender.Infrastructure" && violation.Target == Sample);
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
